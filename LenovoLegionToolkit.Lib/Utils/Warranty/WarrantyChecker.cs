using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Utils.Warranty;

public class WarrantyChecker(ApplicationSettings settings, HttpClientFactory httpClientFactory)
{
    public async Task<WarrantyInfo?> GetWarrantyInfo(MachineInformation machineInformation, CultureInfo cultureInfo, bool forceRefresh = false, CancellationToken token = default)
    {
        if (!forceRefresh && settings.Store.WarrantyInfo.HasValue)
            return settings.Store.WarrantyInfo.Value;

        using var httpClient = httpClientFactory.Create();
        httpClient.Timeout = TimeSpan.FromSeconds(5);

        WarrantyInfo? warrantyInfo = null;

        if (cultureInfo.IetfLanguageTag.Equals("zh-Hans") || machineInformation.Properties.IsChineseModel)
        {
            warrantyInfo = await GetStandardWarrantyInfoForChineseModel(httpClient, machineInformation, token).ConfigureAwait(false);
        }
        else
        {
            warrantyInfo = await GetStandardWarrantyInfo(httpClient, machineInformation, token).ConfigureAwait(false);
        }

        settings.Store.WarrantyInfo = warrantyInfo;
        settings.SynchronizeStore();

        return warrantyInfo;
    }

    private static async Task<WarrantyInfo?> GetStandardWarrantyInfo(HttpClient httpClient, MachineInformation machineInformation, CancellationToken token)
    {
        var content = JsonContent.Create(new { serialNumber = machineInformation.SerialNumber, machineType = machineInformation.MachineType });

        try
        {
            using var response = await httpClient.PostAsync("https://pcsupport.lenovo.com/dk/en/api/v4/upsell/redport/getIbaseInfo", content, token).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var node = JsonNode.Parse(responseContent);

            if (node is null || node["code"]?.GetValue<int>() != 0)
                return null;

            var baseWarranties = node["data"]?["baseWarranties"]?.AsArray() ?? [];
            var upgradeWarranties = node["data"]?["upgradeWarranties"]?.AsArray() ?? [];

            var startDate = baseWarranties.Concat(upgradeWarranties)
                .Select(n => n?["startDate"])
                .Where(n => n is not null)
                .Select(n => DateTime.Parse(n!.ToString(), CultureInfo.InvariantCulture))
                .Min();
            var endDate = baseWarranties.Concat(upgradeWarranties)
                .Select(n => n?["endDate"])
                .Where(n => n is not null)
                .Select(n => DateTime.Parse(n!.ToString(), CultureInfo.InvariantCulture))
                .Max();

            var productString = await httpClient.GetStringAsync(
                $"https://pcsupport.lenovo.com/dk/en/api/v4/mse/getproducts?productId={machineInformation.SerialNumber}",
                token
            ).ConfigureAwait(false);

            var productNode = JsonNode.Parse(productString);
            var firstProductNode = (productNode as JsonArray)?.FirstOrDefault();
            var id = firstProductNode?["Id"];
            var link = id is null ? null : new Uri($"https://pcsupport.lenovo.com/products/{id}");

            return new WarrantyInfo(startDate, endDate, link);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<WarrantyInfo?> GetStandardWarrantyInfoForChineseModel(HttpClient httpClient, MachineInformation machineInformation, CancellationToken token)
    {
        var url = $"https://newsupport.lenovo.com.cn/api/drive/{machineInformation.SerialNumber}/drivewarrantyinfo";
        var response = await httpClient.GetAsync(url, token).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

        var settings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
            }
        };

        var result = await Task.Run(() =>
            JsonConvert.DeserializeObject<WarrantyResponse>(responseContent, settings), token)
            .ConfigureAwait(false);

        var baseWarranties = result?.Data?.BaseInfo ?? Enumerable.Empty<WarrantyItem>();
        var allDetailWarranties = result?.Data?.DetailInfo?.Warranty ?? Enumerable.Empty<WarrantyItem>();
        var allOtherServices = result?.Data?.DetailInfo?.Other ?? Enumerable.Empty<WarrantyItem>();
        var allWarrantyItems = baseWarranties.Concat(allDetailWarranties).Concat(allOtherServices).Concat(result?.Data?.DetailInfo?.OnSite ?? Enumerable.Empty<WarrantyItem>());

        List<string> prooducts = new()
        {
            "笔记本标准服务",                              // Standard
            "消费笔记本二年全面保修送修",                  // Year 2
            "Lenovo Care 智",                              // Lenovo Care
        };

        List<string> excludedProductPartNames = new()
        {
            "综合软件支持",
            "7*24技术支持服务",
            "一年一次到店体检",
            "专属人工 ",
            "硬盘不回收",
        };

        DateTime? startDate = allWarrantyItems
                .Where(w => w.ServiceProductName == prooducts[0])
                .Select(w => DateTime.TryParse(w.StartDateString, CultureInfo.InvariantCulture, out var parsedDate) ? parsedDate : (DateTime?)null)
                .Where(d => d.HasValue)
                .Min();

        var extendedProducts = prooducts.Skip(1).ToList();

        DateTime? endDate = allWarrantyItems
            .Where(w => !string.IsNullOrEmpty(w.ServiceProductName) && extendedProducts.Any(
                shortName => w.ServiceProductName.Contains(shortName) &&
                !excludedProductPartNames.Any(excludedName => w.ServiceProductName.Contains(excludedName))
            ))
            .Select(w => DateTime.TryParse(w.EndDateString, CultureInfo.InvariantCulture, out var parsedDate) ? parsedDate : (DateTime?)null)
            .Where(d => d.HasValue)
            .Max();

        var link = new Uri($"https://newsupport.lenovo.com.cn/deviceGuarantee.html?fromsource=deviceGuarantee&machine={machineInformation.SerialNumber}");

        return new WarrantyInfo(startDate, endDate, link);
    }
}
