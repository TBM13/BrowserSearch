using System;
using System.Collections.Generic;
using Wox.Plugin;

namespace BrowserSearch
{
    internal interface IBrowser
    {
        string Name { get; }
        Dictionary<string, string> Predictions { get; }
        void Init();
        List<Result> GetHistory();
    }
}
