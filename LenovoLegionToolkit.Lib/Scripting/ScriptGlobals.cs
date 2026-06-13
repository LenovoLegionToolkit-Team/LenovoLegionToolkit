using System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Scripting;

public class ScriptGlobals
{
    public void Log(FormattableString message)
    {
        if (Utils.Log.Instance.IsTraceEnabled)
            Utils.Log.Instance.Trace(message, file: "ScriptEngine", lineNumber: 0, caller: "ExecuteAsync");

        Console.WriteLine(message.ToString());
    }
}
