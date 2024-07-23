﻿using System.Collections.Generic;
using Wox.Plugin;

namespace BrowserSearch.Browsers
{
    internal interface IBrowser
    {
        void Init();
        List<Result> GetHistory();
        int CalculateExtraScore(string query, string title, string url);
    }
}
