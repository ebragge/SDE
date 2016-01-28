using System;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
#if _CREATE_WIFI_CONNECTION
    using Windows.Devices.WiFi;
#endif
using Windows.Devices.Enumeration;
using Windows.Foundation;

using Windows.Storage;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.System.Diagnostics;
using System.Collections.Generic;

using LibAudio;
using LibIoTHub;

#if _CREATE_WIFI_CONNECTION
    using Windows.Security.Credentials;
    using LibAccess;
#endif

namespace SDE
{
    public sealed partial class MainPage : Page
    {
        private static int SEND_INTERVAL = 180;

        private object accessLock = new object();

#if _CREATE_WIFI_CONNECTION
        WiFiAdapter wifi = null;
#endif

        private WASAPIEngine audioEngine;
        private IotHubClient client = new IotHubClient();

        private DispatcherTimer startTimer = new DispatcherTimer();
        private DispatcherTimer sendTimer = new DispatcherTimer();
        private DispatcherTimer statusTimer = new DispatcherTimer();

        private uint buffering = 0;
        private uint startCounter = 15;
        private uint samples = 0;
        private uint networkError = 0;
        private uint beat = 0;
        private string status = "";
        private int timeTick = SEND_INTERVAL;

        private bool reboot = false;
        private bool rebootPending = false;

        private ulong lastTimeStamp = 0;

        public MainPage()
        {
            InitializeComponent();

            sendTimer.Interval = new TimeSpan(0, 5, 0); 
            sendTimer.Tick += Send;
            sendTimer.Start();

            statusTimer.Interval = new TimeSpan(0, 0, 1);
            statusTimer.Tick += (object sender, object e) => {
                lock(accessLock)
                {
                    if (timeTick == SEND_INTERVAL) AddStatusMessage();
                    if (timeTick > 0) timeTick--; 

                    AppStatus.Text = status + " "  + timeTick.ToString() + " " + buffering.ToString() + " " + networkError.ToString() + " " 
                                    + rebootPending.ToString() + " " + client.Messages().ToString();
                }
            };
            statusTimer.Start();

            startTimer.Interval = new TimeSpan(0, 0, 1);
            startTimer.Tick += Tick;
            startTimer.Start();

            SetLabels(HeartBeatType.STARTING);
            EmptyTexts("STARTING");

            AddStatusMessage();
        }

        ~MainPage()
        {
            audioEngine.Finish();
        }
        private void ThreadDelegate(HeartBeatType t, TimeWindow w , TDESample[] tde, AudioSample[] a)
        {
            try
            {
                var ign = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    lock(accessLock)
                    {
                        beat++;
                        if (w != null && tde != null && a != null)
                        {
                            var l = w.End();
                            if (t == HeartBeatType.BUFFERING)
                            {
                                l = w.Begin();
                                UpdateUI(t, w, tde, a);
                            }
                            UpdateUI(t, w, tde, a);
                            //client.AddMessage(t, cc0, asdf0, peak0, max0, max1, ave0, ave1, ave_all, beat, b - lastTimeStamp, l - b);
                            lastTimeStamp = l;
                        }
                    }
                });
            }
            catch (Exception ex)            {
                Error("ThreadDelegate: " + ex.ToString() + " " + ex.Message + " " + ex.HResult);
            }        
        }

        private void AddStatusMessage()
        {
            long l1 = 0, l2 = 0, l3 = 0;
            ulong ul1 = 0, ul2 = 0, ul3 = 0, ul4 = 0;

            ul1 = MemoryManager.AppMemoryUsage;
            ul2 = MemoryManager.AppMemoryUsageLimit;

            IReadOnlyList<ProcessDiagnosticInfo> list = ProcessDiagnosticInfo.GetForProcesses();
            foreach (var item in list)
            {
                if (item.ExecutableFileName == "SDE.exe")
                {
                    ProcessCpuUsageReport r = item.CpuUsage.GetReport();
                    l1 = (long)(100 * (r.UserTime.TotalMilliseconds / (r.UserTime.TotalMilliseconds + r.KernelTime.TotalMilliseconds)));
                    ul3 = item.MemoryUsage.GetReport().PeakVirtualMemorySizeInBytes;
                    ul4 = item.MemoryUsage.GetReport().PeakPageFileSizeInBytes;
                }
            }
            beat++;
            client.AddMessage(HeartBeatType.STATUS, l1, l2, l3, ul1, ul2, 0, 0, 0, beat, ul3, ul4);
        }

        private void UpdateUI(HeartBeatType t, TimeWindow w , TDESample[] tde, AudioSample[] a)
        {
            try
            {
                SetLabels(t);
                text1.Text = client.HeartBeatText(t);
                text2.Text = beat.ToString();

                text03.Text = tde[0].Value().ToString();
                text04.Text = tde[1].Value().ToString();
                text05.Text = tde[2].Value().ToString();
                text06.Text = a[1].Align().ToString();
                text07.Text = a[1].MaxAmplitude().ToString();
                text08.Text = a[1].Average().ToString();

                text13.Text = tde[3].Value().ToString();
                text14.Text = tde[4].Value().ToString();
                text15.Text = tde[5].Value().ToString();
                text16.Text = a[2].Align().ToString();
                text17.Text = a[2].MaxAmplitude().ToString();
                text18.Text = a[2].Average().ToString();

                text23.Text = tde[6].Value().ToString();
                text24.Text = tde[7].Value().ToString();
                text25.Text = tde[8].Value().ToString();
                text06.Text = a[3].Align().ToString();
                text07.Text = a[3].MaxAmplitude().ToString();
                text08.Text = a[3].Average().ToString();

                switch (t)
                {
                    case HeartBeatType.DATA:
                    case HeartBeatType.INVALID:
                    case HeartBeatType.SILENCE:
                        buffering = 0;
                        rebootPending = false;
                        break;
                    case HeartBeatType.BUFFERING:
                        buffering++;
                        break;
                    case HeartBeatType.DEVICE_ERROR:
                        rebootPending = true;
                        ResetEngine(10, "ERROR");
                        break;
                    case HeartBeatType.NODEVICE:
                        rebootPending = true;
                        break;
                }

                if (buffering == 30) { rebootPending = true; }
                else if (buffering >= 10 && buffering % 10 == 0) { ResetEngine(10, "RESET"); }
                
                if (t != HeartBeatType.BUFFERING) canvas.Children.Clear();
                if (t == HeartBeatType.DATA)
                {
                    SoundDirection(w.Samples(), 0.3, -1 * tde[0].Value(), 600, 200, 0, 200, new SolidColorBrush(Colors.Red), 4);
                    SoundDirection(w.Samples(), 0.3, -1 * tde[1].Value(), 600, 200, 0, 200, new SolidColorBrush(Colors.Green), 2);
                    SoundDirection(w.Samples(), 0.3, -1 * tde[2].Value(), 600, 200, 0, 200, new SolidColorBrush(Colors.Blue), 1);
                    samples++;       
                }
                text9.Text = samples.ToString();
            }
            catch (Exception ex)
            {
                Error("UpdateUI: " + ex.ToString() + " " + ex.Message + " " + ex.HResult);
            }
        }

        void SoundDirection(double rate, double dist, int delay, int x, int y, double a, int length, SolidColorBrush color, int thickness)
        {
            var val = delay / rate * 343.2 / dist;
            if (val > 1) val = 1;
            if (val < -1) val = -1;
            var ang = Math.Asin(val) + a;

            var line = new Line()
            {
                X1 = x,
                Y1 = y,
                X2 = x + Math.Sin(ang) * length,
                Y2 = y + Math.Cos(ang) * length,
                Stroke = color,
                StrokeThickness = thickness
            };

            canvas.Children.Add(line);
        }

        private async void Tick(object sender, object e)
        {
            try
            {
                if (startCounter == 10)
                {
                    startCounter--;
                    startTimer.Stop();
                    if (beat == 0)
                    {
                        reboot = await ReadStatus();
                        if (reboot) await WriteStatus(false);
                        await client.LoadQueueAsync();
                    }
                    AudioDeviceStatus();
                    startTimer.Start();
                    text2.Text = startCounter.ToString();     
                }
                else if (startCounter == 0)
                {
                    if (reboot)
                    {
                        startTimer.Stop();
                        await client.SaveQueueAsync();
                        reboot = false;
                        LibRPi.HelperClass hc = new LibRPi.HelperClass();
                        hc.Restart();
                    }
                    else
                    {
                        text1.Text = "STARTED";
                        errorText.Text = "";    
                        startTimer.Stop();

                        audioEngine = new WASAPIEngine();

                        var channels = new AudioChannel[2]
                        {
                            new AudioChannel(0, 0, ""),
                            new AudioChannel(0, 1, "")
                        };
    
                        var devParam = new AudioDevices(channels, 1);
                        var param = new AudioParameters(50000, 200, 50, 250, -50, true, true, "sample.txt", 1000, 212);
                        await audioEngine.InitializeAsync(ThreadDelegate, devParam, param);
                    }
                }
                else
                {
                    text2.Text = startCounter.ToString();
                    startCounter--;
                }
            }
            catch (Exception ex)
            {
                Error("Tick: " + ex.ToString() + " " + ex.Message + " " + ex.HResult);
            }
        }

        private void SetStatus(string str)
        {
            lock(accessLock)
            {
                status = str;
            }
        }


#if _CREATE_WIFI_CONNECTION
        private async void Send(object sender, object e)
#else
        private void Send(object sender, object e)
#endif
        {
            timeTick = SEND_INTERVAL;
            if (client.Messages() == 0) return;
#if _CREATE_WIFI_CONNECTION
            SetStatus("RequestAccessAsync");

            var access = await WiFiAdapter.RequestAccessAsync();
            if (access == WiFiAccessStatus.Allowed)
            {
                var wifiDevices = await DeviceInformation.FindAllAsync(WiFiAdapter.GetDeviceSelector());
                if (wifiDevices?.Count > 0)
                {
                    wifi = await WiFiAdapter.FromIdAsync(wifiDevices[0].Id);

                    await WriteStatus(true);
                    await client.SaveQueueAsync();

                    SetStatus("ScanAsync");
                    IAsyncAction a = wifi?.ScanAsync();
                    await a;

                    await WriteStatus(false);

                    if (a.Status == AsyncStatus.Completed && wifi?.NetworkReport?.AvailableNetworks?.Count > 0)
                    {
                        foreach (var network in wifi.NetworkReport.AvailableNetworks)
                        {
                            bool found = false;
                            uint wlan = 0;
                            for (uint i = 0; i < Access.Networks; i++)
                            {
                                if (network.Ssid == Access.SSID(i))
                                {
                                    wlan = i;   
                                    found = true;
                                    break;
                                }
                            }
                            if (found)
                            {
                                var passwordCredential = new PasswordCredential();
                                passwordCredential.Password = Access.WIFI_Password(wlan);

                                SetStatus("ConnectAsync");
                                var result = await wifi.ConnectAsync(network, WiFiReconnectionKind.Automatic, passwordCredential);

                                if (result.ConnectionStatus.Equals(WiFiConnectionStatus.Success))
                                {
#endif
                                    try
                                    {
                                        AsyncActionCompletedHandler handler = (IAsyncAction asyncInfo, AsyncStatus asyncStatus) =>
                                        {
                                            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                                            {
                                                switch (asyncStatus)
                                                {
                                                    case AsyncStatus.Completed:
                                                        errorText.Text = client.Sent() + " Messages sent";
                                                        SetStatus("OK");
                                                        networkError = 0;
                                                        break;
                                                    case AsyncStatus.Canceled:
                                                    case AsyncStatus.Error:
                                                        errorText.Text = "Send: " + asyncInfo.ErrorCode;
                                                        SetStatus("Error");
                                                        break;
                                                }
                                                if (rebootPending) reboot = true;
#if _CREATE_WIFI_CONNECTION
                                                wifi.Disconnect();
                                                wifi = null;
#endif
                                            });
                                        };            
                                        SetStatus("Sending messages");
                                        client.SendMessagesAsync(handler);       
                                    }
                                    catch (Exception ex)
                                    {
                                        Error("Send: " + ex.ToString() + " " + ex.Message + " " + ex.HResult);
                                    }
#if _CREATE_WIFI_CONNECTION
                                }
                                else NoConnection(false, result.ConnectionStatus.ToString());
                                return;
                            }
                        }
                        NoConnection(rebootPending  || (++networkError == 5), Access.Ssid + " not found.");
                    }
                    else NoConnection(rebootPending || (++networkError == 5), "No wifi networks found " + a.Status.ToString());
                }   
                else NoConnection(true, "No wifi adapter found");                    
            }
            else NoConnection(true, "Wifi access denied" + access.ToString());
#endif
        }

        private void NoConnection(bool boot, string str)
        {
            if (!reboot) reboot = boot;
            errorText.Text = str;
            if (!reboot)
            {
                SetStatus("Connection error");
            }
            else ResetEngine(10, "REBOOT");
        }

        private void ResetEngine(uint counter, string str)
        {
            if (startTimer.IsEnabled) startTimer.Stop();
            if (audioEngine != null) audioEngine.Finish();
            audioEngine = null;

            client.AddMessage(HeartBeatType.DEVICE_ERROR, buffering, networkError, 0, 0, 0, 0, 0, 0, 0, 0, 0);          

            EmptyTexts(str);
            startCounter = counter;
            startTimer.Start();
            GC.Collect();
        }

        private void Error(string err)
        {
            errorText.Text = err;
            label0.Text = label1.Text = label2.Text = label3.Text = label4.Text = label5.Text = label6.Text = label7.Text = label8.Text = label9.Text = "";
            EmptyTexts("");
        }

        private void EmptyTexts(string status)
        {
            text1.Text = status;
            text2.Text = text03.Text = text04.Text = text05.Text = text06.Text = text07.Text = text08.Text = text9.Text = 
                         text13.Text = text14.Text = text15.Text = text16.Text = text17.Text = text18.Text =
                         text23.Text = text24.Text = text25.Text = text26.Text = text27.Text = text28.Text = "";
            canvas.Children.Clear();
        }

        private void SetLabels(HeartBeatType t)
        {
            label1.Text = "STATUS";
            switch (t)
            {
                case HeartBeatType.STARTING:
                case HeartBeatType.DEVICE_ERROR:
                case HeartBeatType.NODEVICE:
                    {
                        label2.Text = "BEAT";
                        label3.Text = "";
                        label4.Text = "";
                        label5.Text = "";
                        label6.Text = "";
                        label7.Text = "";
                        label8.Text = "";
                        label9.Text = "";
                        break;
                    };       
                case HeartBeatType.DATA:
                    {
                        label2.Text = "BEAT";
                        label3.Text = "CC";
                        label4.Text = "ASDF";
                        label5.Text = "PEAK";
                        label6.Text = "ALIGN";
                        label7.Text = "MAX";
                        label8.Text = "AVERAGE";
                        label9.Text = "SAMPLES";
                        break;
                    };
                case HeartBeatType.INVALID:
                case HeartBeatType.SILENCE:
                    {
                        label2.Text = "BEAT";
                        label3.Text = "";
                        label4.Text = "";
                        label5.Text = "";
                        label6.Text = "";
                        label7.Text = "MAX";
                        label8.Text = "AVERAGE";
                        label9.Text = "SAMPLES";
                        break;
                    };
                case HeartBeatType.BUFFERING:
                    {
                        label2.Text = "BEAT";
                        label3.Text = "PAC";
                        label4.Text = "DIS";
                        label5.Text = "REM";
                        label6.Text = "";
                        label7.Text = "TIMESTAMP 0";
                        label8.Text = "TIMESTAMP 1";
                        label9.Text = "SAMPLES";
                        break;
                    }
            }
        }

        private async Task WriteStatus(bool state)
        {
            StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
            StorageFile file = await storageFolder.CreateFileAsync("status.txt", CreationCollisionOption.ReplaceExisting);
            var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);

            using (var outputStream = stream.GetOutputStreamAt(0))
            {
                using (var dataWriter = new DataWriter(outputStream))
                {
                    dataWriter.WriteBoolean(state);
                    await dataWriter.StoreAsync();
                    await outputStream.FlushAsync();
                }
            }
            stream.Dispose();
        }

        private async Task<bool> ReadStatus()
        {
            bool state = false;
            try
            {
                StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await storageFolder.GetFileAsync("status.txt");

                var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                ulong size = stream.Size;

                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    using (var dataReader = new DataReader(inputStream))
                    {
                        uint numBytesLoaded = await dataReader.LoadAsync((uint)size);
                        state = dataReader.ReadBoolean();
                    }
                }
                stream.Dispose();
            }
            catch (Exception) {}
            return state;
        }

        private async void AudioDeviceStatus()
        {
            string s = "";
            var dis1 = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            foreach (var item in dis1)
            {
                object o1, o2;
                System.Collections.Generic.IReadOnlyDictionary<string,object> p = item.Properties;
                p.TryGetValue("System.ItemNameDisplay", out o1);
                p.TryGetValue("System.Devices.InterfaceEnabled", out o2);
                s += (o1 != null ? o1.ToString() : "") + " " + (o2 != null ? o2.ToString() : "") + "\n";
            }
            errorText.Text = s;
        }
    }
}
