using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.Foundation;

using LibAudio;

namespace LibIoTHub
{
    public delegate void MsgHandler(Object o, String s);

    public sealed class IotHubClient
    {
        private sealed class DataPoint
        {
            public HeartBeatType t;
            public DateTime time;
            public long cc;
            public long asdf;
            public long peak;
            public ulong max0;
            public ulong max1;
            public ulong ave0;
            public ulong ave1;
            public ulong ave;
            public ulong beat;
            public ulong noAudio;
            public ulong audio;

            public void Write(DataWriter writer)
            {
                writer.WriteInt32((int)t);
                writer.WriteDateTime(time);
                writer.WriteInt64(cc);
                writer.WriteInt64(asdf);
                writer.WriteInt64(peak);
                writer.WriteUInt64(max0);
                writer.WriteUInt64(max1);
                writer.WriteUInt64(ave0);
                writer.WriteUInt64(ave1);
                writer.WriteUInt64(ave);
                writer.WriteUInt64(beat);
                writer.WriteUInt64(noAudio);
                writer.WriteUInt64(audio);
            }

            public void Read(DataReader reader)
            {
                t = (HeartBeatType)reader.ReadInt32();
                time = reader.ReadDateTime().UtcDateTime;
                cc = reader.ReadInt64();
                asdf = reader.ReadInt64();
                peak = reader.ReadInt64();
                max0 = reader.ReadUInt64();
                max1 = reader.ReadUInt64();
                ave0 = reader.ReadUInt64();
                ave1 = reader.ReadUInt64();
                ave = reader.ReadUInt64();
                beat = reader.ReadUInt64();
                audio = reader.ReadUInt64();
                noAudio = reader.ReadUInt64();
            }
        }

        private static string FILE_NAME = "queue.dat";
        private int MAX_MESSAGES = 10000;
        private int MAX_BATCH = 1000;

        private object thisLock = new object();
        private int msgCount = 0;

        System.Collections.Generic.Queue<DataPoint> queue = new System.Collections.Generic.Queue<DataPoint>();
        Task task = null;

        public IotHubClient()
        {
        }

        public int Messages()
        {
            int count = 0;
            lock (thisLock)
            {
                count = queue.Count;
            }
            return count;
        }

        public int Sent()
        {
            int tmp;
            lock (thisLock)
            {
                for (int i = 0; i < msgCount; i++) queue.Dequeue();
                tmp = msgCount;
                msgCount = 0;
            }
            return tmp;
        }

        public IAsyncAction SaveQueueAsync()
        {
            Func<Task> action = async () =>
            {
                await SaveQueueInternalAsync(this);
            };
            return action().AsAsyncAction();
        }

        public IAsyncAction LoadQueueAsync()
        {
            Func<Task> action = async () =>
            {
                await LoadQueueInternalAsync(this);
            };
            return action().AsAsyncAction();
        }

        private static async Task SaveQueueInternalAsync(IotHubClient client)
        {
            try
            {
                System.Collections.Generic.Queue<DataPoint> tmp = null;
                lock (client.thisLock)
                {
                    tmp = new System.Collections.Generic.Queue<DataPoint>(client.queue);
                }
                StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await storageFolder.CreateFileAsync(FILE_NAME, CreationCollisionOption.ReplaceExisting);
                var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);

                using (var outputStream = stream.GetOutputStreamAt(0))
                {
                    using (var dataWriter = new DataWriter(outputStream))
                    {
                        dataWriter.WriteInt32(tmp.Count);
                        foreach (var item in tmp) { item.Write(dataWriter); }
                        await dataWriter.StoreAsync();
                        await outputStream.FlushAsync();
                    }
                }
                stream.Dispose();
            }
            catch (Exception) { }
        }

        private static async Task LoadQueueInternalAsync(IotHubClient client)
        {
            System.Collections.Generic.Queue<DataPoint> tmp = new System.Collections.Generic.Queue<DataPoint>();
            try
            {
                StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await storageFolder.GetFileAsync(FILE_NAME);

                var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                ulong size = stream.Size;

                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    using (var dataReader = new DataReader(inputStream))
                    {
                        uint numBytesLoaded = await dataReader.LoadAsync((uint)size);
                        int count = dataReader.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            DataPoint p = new DataPoint();
                            p.Read(dataReader);
                            tmp.Enqueue(p);
                        }
                    }

                }
                stream.Dispose();
                lock (client.thisLock)
                {
                    client.queue.Clear();
                    client.queue = null;
                    client.queue = new System.Collections.Generic.Queue<DataPoint>(tmp);
                }
            }
            catch (Exception) { }
        }

        public void AddMessage(HeartBeatType t, long cc, long asdf, long peak, ulong max0, ulong max1, ulong ave0, ulong ave1, ulong ave, ulong beat, ulong noAudio, ulong audio)
        {
            DataPoint p = new DataPoint();
            p.time = DateTime.UtcNow;
            p.t = t;
            p.cc = cc;
            p.asdf = asdf;
            p.peak = peak;
            p.max0 = max0;
            p.max1 = max1;
            p.ave0 = ave0;
            p.ave1 = ave1;
            p.ave = ave;
            p.beat = beat;
            p.noAudio = noAudio;
            p.audio = audio;

            lock (thisLock)
            {
                if (t == HeartBeatType.DATA)
                {
                    if (queue.Count == MAX_MESSAGES) queue.Dequeue();
                    queue.Enqueue(p);
                }
                else if (queue.Count < MAX_MESSAGES) queue.Enqueue(p);
            }
        }

        public bool SendMessagesAsync(Windows.Foundation.AsyncActionCompletedHandler handler)
        {
            int count = 0;
            lock (thisLock)
            {
                count = queue.Count;
            }
            if (count > 0)
            {
                Func<Task> action = async () =>
                {
                    System.Collections.Generic.Queue<Message> q = new System.Collections.Generic.Queue<Message>();
                    lock (thisLock)
                    {
                        int i = 0;
                        foreach (DataPoint p in queue)
                        {
                            string s = HeartBeatText(p.t);
                            var telemetryDataPoint = new
                            {
                                ID = Access.DeviceID,
                                TIME = p.time,
                                MSG = s,
                                CC = p.cc,
                                ASDF = p.asdf,
                                PEAK = p.peak,
                                MAX0 = p.max0,
                                MAX1 = p.max1,
                                AVE0 = p.ave0,
                                AVE1 = p.ave1,
                                AVE = p.ave,
                                BEAT = p.beat,
                                NOAUDIO = p.noAudio,
                                AUDIO = p.audio
                            };
                            var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
                            var message = new Message(Encoding.ASCII.GetBytes(messageString));
                            q.Enqueue(message);
                            if (++i == MAX_BATCH) break;
                        }
                        msgCount = Math.Min(queue.Count, MAX_BATCH);
                    }
                    var auth = new DeviceAuthenticationWithRegistrySymmetricKey(Access.DeviceID, Access.DeviceKey);
                    DeviceClient deviceClient = DeviceClient.Create(Access.IoTHubUri, auth, TransportType.Http1);
                    await deviceClient.OpenAsync();
                    IAsyncAction a = deviceClient.SendEventBatchAsync(q);
                    a.Completed = handler;
                    await a;
                    await deviceClient.CloseAsync();
                };
                task = Task.Factory.StartNew(action);
                return true;
            }
            else return false;
        }

        public string HeartBeatText(HeartBeatType t)
        {
            switch (t)
            {
                case HeartBeatType.DATA: return "DATA";
                case HeartBeatType.INVALID: return "INVALID";
                case HeartBeatType.SILENCE: return "SILENCE";
                case HeartBeatType.BUFFERING: return "BUFFERING";
                case HeartBeatType.DEVICE_ERROR: return "ERROR";
                case HeartBeatType.NODEVICE: return "NO DEVICES";
                case HeartBeatType.STATUS: return "STATUS";
                default: return "";
            }
        }
    }
}
