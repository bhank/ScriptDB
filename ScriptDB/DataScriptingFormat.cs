using System;

namespace ScriptDb
{
    [Flags]
    public enum DataScriptingFormat
    {
        None = 0,
        Sql = 1,
        Csv = 2,
        Bcp = 4,
        All = Sql | Csv | Bcp,
    }
}
