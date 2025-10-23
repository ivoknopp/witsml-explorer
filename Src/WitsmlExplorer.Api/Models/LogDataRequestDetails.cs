using System;
using System.Collections.Generic;

namespace WitsmlExplorer.Api.Models
{
    public class LogDataRequestDetails
    {
        public Dictionary<string, List<string>> Mnemonics { get; set; } = new Dictionary<string, List<string>>();
        public string UserText { get; set; } = string.Empty;
    }
}
