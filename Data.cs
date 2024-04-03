using COSMP.Network;
using System;
using System.Collections.Generic;

namespace COSMP
{
    [Serializable]
    public class GlobalData
    {
        public string username = "";
        public string host = COSMPConstants.DEFAULT_SERVER_HOST;
        public int port = COSMPConstants.DEFAULT_SERVER_PORT;

        public List<string> banned = [];
    }
}