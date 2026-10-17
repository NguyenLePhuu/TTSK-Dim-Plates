using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TTSK_AutoDim_Plates.MeetingTranslation
{
    // Gom hai transcript vào đoạn hiển thị chung; không gọi dịch lại hoặc đoán người nói.
    internal sealed class GoogleLiveTranscriptRows
    {
        internal const string Provider = "Google Live";
        internal const string CredentialTarget = "TTSKDimPlates/GOOGLE_LIVE";
        private readonly StringBuilder source = new StringBuilder(), translated = new StringBuilder();
        private readonly Guid session = Guid.NewGuid();
        private long sequence;
        private int revision;
        private DateTimeOffset started, updated;
        internal event Action<MeetingTranslationLine> Changed;
        internal static bool IsLive(MeetingTranslationLine line) => line.ProviderName == Provider;
        internal static bool IsSource(MeetingTranslationLine line) => IsLive(line) && string.IsNullOrWhiteSpace(line.Vietnamese);
        internal static string Label(MeetingTranslationLine line) => IsLive(line) ? "Google Live" : line.DisplayTiming;
        internal static string Detail(MeetingTranslationLine line) => IsLive(line)
            ? "Google Live · " + line.RecognizedAt.ToLocalTime().ToString("HH:mm:ss") + " · Nguồn nhận trong cùng đoạn; Google có thể trả hai ngôn ngữ lệch thời điểm."
            : line.TimingSummary;

        private static bool SentenceEnded(StringBuilder text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || "\"'”’」』)）".IndexOf(c) >= 0) continue;
                return "。！？!?".IndexOf(c) >= 0 || (c == '.' && (i == 0 || !char.IsDigit(text[i - 1])));
            }
            return false;
        }
        internal void Append(string text, bool vietnamese, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(text)) return;
            Tick(now);
            foreach (char c in text)
            {
                bool both = source.Length > 0 && translated.Length > 0;
                var buffer = vietnamese ? translated : source;
                int length = Math.Max(source.Length, translated.Length);
                bool boundary = char.IsWhiteSpace(c) || (buffer.Length > 0 && "。！？!?".IndexOf(buffer[buffer.Length - 1]) >= 0);
                // Chỉ chốt chung khi cả hai luồng hết câu. Đoạn dài ngắt ở biên chữ, giữ cặp surrogate.
                if (!char.IsLowSurrogate(c) && ((both && SentenceEnded(source) && SentenceEnded(translated) && !char.IsWhiteSpace(c))
                    || (both && length >= 480 && boundary) || length >= 1200)) Finish(now);
                if (source.Length == 0 && translated.Length == 0)
                {
                    if (char.IsWhiteSpace(c)) continue;
                    ++sequence; revision = 0; started = now;
                }
                buffer.Append(c); updated = now;
            }
            Publish(now);
        }
        internal void Tick(DateTimeOffset now)
        {
            // Giây chờ chỉ chốt UI, không dừng audio. Chờ dài hơn nếu bản dịch chưa tới.
            bool both = source.Length > 0 && translated.Length > 0;
            if ((source.Length > 0 || translated.Length > 0) && (now - updated).TotalMilliseconds >= (both ? 1800 : 10000)) Finish(now);
        }
        internal void Finish(DateTimeOffset now)
        {
            Publish(now); source.Clear(); translated.Clear(); revision = 0;
        }
        private void Publish(DateTimeOffset now)
        {
            if (source.Length == 0 && translated.Length == 0) return;
            Changed?.Invoke(new MeetingTranslationLine(sequence, source.ToString(), translated.ToString(), started, now, 0)
            {
                SessionId = session, Revision = ++revision, ProviderName = Provider,
                DeliveryRoute = "live-paragraph", TranslationSucceeded = translated.Length > 0
            });
        }
    }

    // Luồng âm thanh riêng: không dùng caption tracker, generateContent hoặc vòng đổi key/model văn bản.
    internal sealed class GoogleLiveTranslationSession
    {
        internal const string Model = "gemini-3.5-live-translate-preview";
        internal const string Endpoint = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";
        internal event Action<string, bool> Transcript;
        internal event Action<string> Status;
        internal event Action<float> Level;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };

        internal static string SetupJson(bool preferJapanese = true)
        {
            return new JavaScriptSerializer().Serialize(new { setup = new { model = "models/" + Model,
                inputAudioTranscription = preferJapanese ? new Dictionary<string, object> { { "languageCodes", new[] { "ja" } } } : new Dictionary<string, object>(),
                outputAudioTranscription = new { },
                generationConfig = new { responseModalities = new[] { "AUDIO" },
                    translationConfig = new { targetLanguageCode = "vi", echoTargetLanguage = false } } } });
        }
        internal static string AudioJson(byte[] pcm)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length % 2 != 0) throw new ArgumentException("PCM16 không hợp lệ.");
            return new JavaScriptSerializer().Serialize(new { realtimeInput = new { audio = new { data = Convert.ToBase64String(pcm), mimeType = "audio/pcm;rate=16000" } } });
        }
        internal static string SafeError(Exception error, string key)
        {
            string text = error is OperationCanceledException ? "Kết nối đã hủy hoặc quá thời gian chờ." : error.GetBaseException().Message;
            if (!string.IsNullOrEmpty(key)) text = text.Replace(key, "[key]");
            return text.Length > 700 ? text.Substring(0, 700) : text;
        }
        private static ClientWebSocket Socket(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace)) throw new InvalidOperationException("Nhập Google API key hợp lệ.");
            if (key.StartsWith("nvapi-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Google Live cần Google API key, không dùng key NVIDIA.");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("x-goog-api-key", key);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20); return socket;
        }
        internal static async Task SendAsync(ClientWebSocket socket, string message, CancellationToken token)
        {
            var data = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        internal static async Task<string> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16384];
            using (var stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException("Google Live đóng kết nối (" + result.CloseStatus + "): " + result.CloseStatusDescription);
                    // Google có thể gửi JSON UTF-8 trong frame Binary, kể cả setupComplete.
                    if (stream.Length + result.Count > 2 * 1024 * 1024)
                        throw new InvalidOperationException("Phản hồi Google Live không hợp lệ hoặc vượt giới hạn 2 MiB.");
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        internal static Dictionary<string, object> ObjectAt(Dictionary<string, object> data, string key)
        { object value; return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        private static string TextAt(Dictionary<string, object> data, string key)
        { object value; return data != null && data.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
        private static void ThrowServerError(Dictionary<string, object> message)
        {
            var error = ObjectAt(message, "error");
            if (error != null) throw new InvalidOperationException("Google Live: " + TextAt(error, "status") + " · " + TextAt(error, "message"));
        }
        private static async Task ConnectAsync(ClientWebSocket socket, CancellationToken token, bool preferJapanese = true)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20)); // Giây cho handshake; chưa thu âm trong giai đoạn này.
                await socket.ConnectAsync(new Uri(Endpoint), timeout.Token).ConfigureAwait(false);
                await SendAsync(socket, SetupJson(preferJapanese), timeout.Token).ConfigureAwait(false);
                var response = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await ReceiveAsync(socket, timeout.Token).ConfigureAwait(false));
                ThrowServerError(response);
                if (response == null || !response.ContainsKey("setupComplete")) throw new InvalidOperationException("Google chưa xác nhận phiên Live Translate.");
            }
        }
        internal static async Task ValidateAsync(string key, CancellationToken token)
        {
            using (var socket = Socket(key))
            {
                try { await ConnectAsync(socket, token).ConfigureAwait(false); }
                finally { socket.Abort(); } // Kiểm quyền mở model, không đọc/gửi âm thanh.
            }
        }
        internal async Task RunAsync(string key, string deviceId, CancellationToken token, bool preferJapanese = true)
        {
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var socket = Socket(key))
            using (var queue = new BlockingCollection<byte[]>(30)) // 3 giây PCM; dừng khi mạng không theo kịp, không bỏ tiếng âm thầm.
            {
                Task capture = null, sender = null, receiver = null;
                using (lifetime.Token.Register(socket.Abort))
                try
                {
                    Status?.Invoke("Đang kết nối " + Model + "…");
                    await ConnectAsync(socket, lifetime.Token, preferJapanese).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    receiver = ReceiveLoopAsync(socket, lifetime.Token);
                    sender = Task.Run(async () =>
                    {
                        foreach (byte[] pcm in queue.GetConsumingEnumerable(lifetime.Token))
                            await SendAsync(socket, AudioJson(pcm), lifetime.Token).ConfigureAwait(false);
                    }, lifetime.Token);
                    capture = Task.Run(() => WindowsLoopback.Capture(deviceId, pcm =>
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        if (!queue.TryAdd(pcm)) throw new InvalidOperationException("Mạng không theo kịp âm thanh. Đã dừng; kiểm tra mạng rồi Bắt đầu lại.");
                    }, level => Level?.Invoke(level), () => Status?.Invoke("Đang nghe âm thanh thiết bị đã chọn → dịch sang tiếng Việt"), lifetime.Token), lifetime.Token);
                    var ended = await Task.WhenAny(capture, sender, receiver).ConfigureAwait(false);
                    await ended.ConfigureAwait(false);
                    if (!token.IsCancellationRequested) throw new InvalidOperationException("Phiên âm thanh đã kết thúc. Bấm Bắt đầu để kết nối lại.");
                }
                finally
                {
                    lifetime.Cancel(); socket.Abort(); queue.CompleteAdding();
                    foreach (var task in new[] { capture, sender, receiver })
                        if (task != null) try { await task.ConfigureAwait(false); } catch { } // Chờ giải phóng capture trước khi UI cho phép mở phiên mới.
                }
            }
        }
        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
                HandleMessage(await ReceiveAsync(socket, token).ConfigureAwait(false));
        }
        internal void HandleMessage(string message)
        {
            var root = json.Deserialize<Dictionary<string, object>>(message); ThrowServerError(root);
            if (root == null) throw new InvalidOperationException("Phản hồi Google Live rỗng.");
            if (root.ContainsKey("goAway")) throw new InvalidOperationException("Google yêu cầu kết thúc phiên Live. Bấm Bắt đầu để mở phiên mới.");
            var content = ObjectAt(root, "serverContent"); if (content == null) return;
            foreach (string field in new[] { "inputTranscription", "outputTranscription" })
            {
                var transcript = ObjectAt(content, field); string text = TextAt(transcript, "text");
                if (text.Length > 0) Transcript?.Invoke(text, field == "outputTranscription");
                object finished;
                if (transcript != null && transcript.TryGetValue("finished", out finished) && Equals(finished, true)) Transcript?.Invoke("\r\n", field == "outputTranscription");
            }
            // Không phát tiếng dịch để tránh thu ngược vào loopback; chỉ hiển thị transcript Google trả về.
            if (content.ContainsKey("turnComplete")) { Transcript?.Invoke("\r\n", false); Transcript?.Invoke("\r\n", true); }
            object interrupted;
            if (content.TryGetValue("interrupted", out interrupted) && Equals(interrupted, true)) Status?.Invoke("Google ngắt đoạn đang dịch; phần chữ đã nhận được giữ lại.");
        }
    }

    internal sealed class LiveAudioDevice
    {
        internal string Id, Name;
        internal bool TeamsActive, DefaultCommunications, DefaultMultimedia;
        internal float Peak;
        public override string ToString() => Name + (TeamsActive ? " · Teams" : DefaultCommunications ? " · Mặc định cuộc gọi" : DefaultMultimedia ? " · Mặc định phát" : "");
    }

    internal sealed class LiveAudioLevel
    {
        private float peak;
        internal void Observe(float value) { peak = Math.Max(peak, value); }
        internal float TakePeak() { float value = peak; peak = 0; return value; }
    }

    // WASAPI loopback shared: chỉ loa/tai nghe được chọn; không micro, không file âm thanh/model Tekla.
    internal static class WindowsLoopback
    {
        internal static IReadOnlyList<LiveAudioDevice> Devices()
        {
            // Session audio cần COM MTA; không mang COM object về luồng giao diện.
            return Task.Run(() => ReadDevices()).GetAwaiter().GetResult();
        }
        internal static LiveAudioDevice RecommendDevice(IEnumerable<LiveAudioDevice> devices)
        {
            return devices.OrderByDescending(d => d.TeamsActive)
                .ThenByDescending(d => d.Peak > .003F).ThenByDescending(d => d.DefaultCommunications)
                .ThenByDescending(d => d.DefaultMultimedia).ThenByDescending(d => d.Peak).FirstOrDefault();
        }
        private static IReadOnlyList<LiveAudioDevice> ReadDevices()
        {
            var result = new List<LiveAudioDevice>(); IMMDeviceEnumerator enumerator = null; IMMDeviceCollection devices = null;
            int com = CoInitializeEx(IntPtr.Zero, 0);
            try
            {
                Check(com);
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator(); Check(enumerator.EnumAudioEndpoints(0, 1, out devices));
                string communications = DefaultDeviceId(enumerator, 2), multimedia = DefaultDeviceId(enumerator, 1);
                uint count; Check(devices.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null; IPropertyStore properties = null;
                    try
                    {
                        Check(devices.Item(i, out device)); string id; Check(device.GetId(out id));
                        Check(device.OpenPropertyStore(0, out properties));
                        var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 }; PropVariant value;
                        Check(properties.GetValue(ref key, out value)); string name;
                        try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : "Thiết bị âm thanh " + (i + 1); }
                        finally { PropVariantClear(ref value); }
                        var info = new LiveAudioDevice { Id = id, Name = name, DefaultCommunications = id == communications, DefaultMultimedia = id == multimedia };
                        ReadActivity(device, info);
                        result.Add(info);
                    }
                    finally { Release(properties); Release(device); }
                }
            }
            finally { Release(devices); Release(enumerator); if (com >= 0) CoUninitialize(); }
            return result;
        }
        private static string DefaultDeviceId(IMMDeviceEnumerator enumerator, int role)
        {
            IMMDevice device = null;
            try { if (enumerator.GetDefaultAudioEndpoint(0, role, out device) < 0) return null; string id; Check(device.GetId(out id)); return id; }
            finally { Release(device); }
        }
        private static void ReadActivity(IMMDevice device, LiveAudioDevice info)
        {
            object meter = null, manager = null; IAudioSessionEnumerator sessions = null;
            try
            {
                var meterId = typeof(IAudioMeterInformation).GUID;
                if (device.Activate(ref meterId, 23, IntPtr.Zero, out meter) >= 0)
                { float peak; if (((IAudioMeterInformation)meter).GetPeakValue(out peak) >= 0) info.Peak = peak; }
                var managerId = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref managerId, 23, IntPtr.Zero, out manager) < 0) return;
                Check(((IAudioSessionManager2)manager).GetSessionEnumerator(out sessions));
                int count; Check(sessions.GetCount(out count));
                for (int i = 0; i < count; i++)
                {
                    object session = null;
                    try
                    {
                        Check(sessions.GetSession(i, out session)); var control = (IAudioSessionControl2)session;
                        int state; uint pid;
                        if (control.GetState(out state) < 0 || state != 1 || control.GetProcessId(out pid) < 0 || pid == 0) continue;
                        using (var process = System.Diagnostics.Process.GetProcessById(checked((int)pid)))
                            if (string.Equals(process.ProcessName, "ms-teams", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(process.ProcessName, "Teams", StringComparison.OrdinalIgnoreCase)) info.TeamsActive = true;
                    }
                    catch (ArgumentException) { } // Tiến trình có thể vừa kết thúc khi đang liệt kê.
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (COMException) { }
                    finally { Release(session); }
                }
            }
            catch (COMException) { } // Metadata phụ không được làm mất danh sách thiết bị vẫn thu được.
            finally { Release(sessions); Release(manager); Release(meter); }
        }
        internal static void Capture(string deviceId, Action<byte[]> consume, Action<float> level, Action started, CancellationToken token)
        {
            IMMDeviceEnumerator enumerator = null; IMMDevice device = null; IAudioClient client = null; IAudioCaptureClient capture = null;
            IntPtr format = IntPtr.Zero; bool running = false; int com = CoInitializeEx(IntPtr.Zero, 0);
            try
            {
                Check(com); token.ThrowIfCancellationRequested();
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator(); Check(enumerator.GetDevice(deviceId, out device));
                object audio; var clientId = typeof(IAudioClient).GUID; Check(device.Activate(ref clientId, 23, IntPtr.Zero, out audio)); client = (IAudioClient)audio;
                Check(client.GetMixFormat(out format)); var converter = new LivePcmConverter(format, consume);
                Check(client.Initialize(0, 0x00020000, 1000000, 0, format, IntPtr.Zero)); // 100ms shared loopback; không đổi âm lượng/thiết bị mặc định.
                object service; var captureId = typeof(IAudioCaptureClient).GUID; Check(client.GetService(ref captureId, out service)); capture = (IAudioCaptureClient)service;
                Check(client.Start()); running = true; started();
                var clock = System.Diagnostics.Stopwatch.StartNew(); long lastLevel = -250, lastPacket = 0, lastSilence = 0; bool silenceSent = false;
                var meter = new LiveAudioLevel(); // Giữ đỉnh mọi packet trong 250ms, không bỏ sót tiếng ngắn giữa hai lần vẽ UI.
                while (!token.IsCancellationRequested)
                {
                    uint frames; Check(capture.GetNextPacketSize(out frames));
                    while (frames > 0)
                    {
                        IntPtr data; uint flags, count; ulong position, counter;
                        Check(capture.GetBuffer(out data, out count, out flags, out position, out counter));
                        float peak;
                        try
                        {
                            if ((flags & 1) != 0 && lastPacket > 0 && clock.ElapsedMilliseconds - lastPacket < 100)
                                throw new InvalidOperationException("Luồng âm thanh bị gián đoạn. Đã dừng để tránh mất tiếng; chọn lại thiết bị rồi Bắt đầu.");
                            peak = converter.Push(data, checked((int)count), (flags & 2) != 0);
                        }
                        finally { Check(capture.ReleaseBuffer(count)); }
                        lastPacket = clock.ElapsedMilliseconds; silenceSent = false;
                        meter.Observe(peak);
                        if (clock.ElapsedMilliseconds - lastLevel >= 250) { level(meter.TakePeak()); lastLevel = clock.ElapsedMilliseconds; }
                        token.ThrowIfCancellationRequested(); Check(capture.GetNextPacketSize(out frames));
                    }
                    // Khi đầu ra ngừng phát, giữ PCM im lặng đúng nhịp 100ms để Google chốt câu.
                    if (clock.ElapsedMilliseconds - lastPacket >= 100 && clock.ElapsedMilliseconds - lastSilence >= 100)
                    { if (!silenceSent) converter.Flush(); consume(new byte[3200]); level(meter.TakePeak()); silenceSent = true; lastSilence = clock.ElapsedMilliseconds; }
                    token.WaitHandle.WaitOne(15);
                }
            }
            finally
            {
                if (running && client != null) client.Stop(); Release(capture); Release(client); Release(device); Release(enumerator);
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format); if (com >= 0) CoUninitialize();
            }
        }
        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { internal Guid Format; internal uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal IntPtr Pointer; }
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceCollection
        { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }
        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioClient
        {
            [PreserveSig] int Initialize(int share, uint flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding); [PreserveSig] int IsFormatSupported(int share, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format); [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
            [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset(); [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
            [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
        }
        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioMeterInformation
        { [PreserveSig] int GetPeakValue(out float peak); }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr session, uint flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr session, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(ref Guid group, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint pid);
        }
    }

    internal sealed class LivePcmConverter
    {
        private readonly int rate, channels, bytes, stride;
        private readonly bool floating;
        private readonly Action<byte[]> consume;
        private readonly byte[] chunk = new byte[3200]; // 100ms mono PCM16 16kHz theo Live API.
        private int used; private long index; private double next; private float previous;
        internal LivePcmConverter(IntPtr format, Action<byte[]> consume)
        {
            int tag = (ushort)Marshal.ReadInt16(format), bits = (ushort)Marshal.ReadInt16(format, 14);
            channels = (ushort)Marshal.ReadInt16(format, 2); rate = Marshal.ReadInt32(format, 4); stride = (ushort)Marshal.ReadInt16(format, 12);
            if (tag == 0xfffe)
            {
                if ((ushort)Marshal.ReadInt16(format, 16) < 22) throw new NotSupportedException("Định dạng âm thanh mở rộng không hợp lệ.");
                var guid = (Guid)Marshal.PtrToStructure(IntPtr.Add(format, 24), typeof(Guid));
                tag = guid == new Guid("00000003-0000-0010-8000-00aa00389b71") ? 3 : guid == new Guid("00000001-0000-0010-8000-00aa00389b71") ? 1 : -1;
            }
            floating = tag == 3; bytes = bits / 8; this.consume = consume;
            if (channels < 1 || channels > 32 || rate < 8000 || rate > 384000 || stride != channels * bytes
                || !(floating && bits == 32 || tag == 1 && (bits == 16 || bits == 24 || bits == 32)))
                throw new NotSupportedException("Thiết bị dùng định dạng chưa hỗ trợ. Chọn đầu ra PCM16/24/32 hoặc Float32.");
        }
        internal float Push(IntPtr pointer, int frames, bool silence)
        {
            if (frames < 0 || frames > rate * 2) throw new InvalidOperationException("Packet âm thanh quá lớn.");
            var data = new byte[checked(frames * stride)]; if (!silence && data.Length > 0) Marshal.Copy(pointer, data, 0, data.Length);
            float peak = 0;
            for (int frame = 0; frame < frames; frame++, index++)
            {
                float sample = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    int offset = frame * stride + channel * bytes; float value;
                    if (floating) value = BitConverter.ToSingle(data, offset);
                    else if (bytes == 2) value = BitConverter.ToInt16(data, offset) / 32768F;
                    else if (bytes == 3) { int n = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16; if ((n & 0x800000) != 0) n |= unchecked((int)0xff000000); value = n / 8388608F; }
                    else value = BitConverter.ToInt32(data, offset) / 2147483648F;
                    sample += float.IsNaN(value) || float.IsInfinity(value) ? 0 : value / channels;
                }
                peak = Math.Max(peak, Math.Abs(sample));
                while (next <= index)
                {
                    float value = index == 0 ? sample : previous + (sample - previous) * (float)(next - (index - 1));
                    short pcm = (short)Math.Round(Math.Max(-1, Math.Min(1, value)) * 32767);
                    chunk[used++] = (byte)pcm; chunk[used++] = (byte)(pcm >> 8);
                    if (used == chunk.Length) { consume((byte[])chunk.Clone()); used = 0; }
                    next += rate / 16000.0;
                }
                previous = sample;
            }
            return Math.Min(1, peak);
        }
        internal void Flush()
        { if (used > 0) { var tail = new byte[used]; Buffer.BlockCopy(chunk, 0, tail, 0, used); consume(tail); used = 0; } }
    }
}
