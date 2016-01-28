namespace LibIoTHub
{
    public sealed class Access
    {
        static string ssid0 = "***";
        static string ssid1 = "***";
        static string ssid2 = "***";
        static string ssid3 = "***";
        static string ssid4 = "***";

        static string wifi_password0 = "***";
        static string wifi_password1 = "***";
        static string wifi_password2 = "***";
        static string wifi_password3 = "***";
        static string wifi_password4 = "***";

        static string iotHubUri = "***";
        static string connectionString = "***";

        static string deviceId = "***";
        static string deviceKey = "***";

        public static string Ssid { get { return ssid0; } }

        public static uint Networks { get { return 5; } }

        public static string SSID(uint index)
        {
            switch (index)
            {
                case 1: return ssid1;
                case 2: return ssid2;
                case 3: return ssid3;
                case 4: return ssid4;
                default: return ssid0;
            }
        }
        public static string Wifi_Password { get { return wifi_password0; } }
        public static string WIFI_Password(uint index)
        {
            switch (index)
            {
                case 1: return wifi_password1;
                case 2: return wifi_password2;
                case 3: return wifi_password3;
                case 4: return wifi_password4;
                default: return wifi_password0;
            }
        }

        public static string IoTHubUri { get { return iotHubUri; } }
        public static string DeviceKey { get { return deviceKey; } }
        public static string DeviceID { get { return deviceId; } }
        public static string ConnectionString { get { return connectionString; } }
    }
}
