using System.Collections.Generic;
using Wox.Plugin;

namespace BrowserSearch
{
    internal interface IBrowser
    {
        Dictionary<string, string> Predictions { get; }
        void Init();
        List<Result> GetHistory();
    }
}
