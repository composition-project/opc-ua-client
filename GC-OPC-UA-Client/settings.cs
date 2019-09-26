using System;
using System.Collections.Generic;
using System.Text;

namespace GC_OPC_UA_Client
{
    public static class settings
    {
        public static string PlantRefId = "";
        public static string CloudLogFolder = "";
        public static string DataBaseFileAndPath = "";
        public static string OPCUAServerAddress = "";
        public static bool AddIDToTagName = false;
        public static bool UseRPiTime = false;
        public static string[] IgnoreTags = null;
    }
}
