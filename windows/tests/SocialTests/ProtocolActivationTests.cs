using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using Lexi;
using Lexi.Social;

internal static class ProtocolActivationTests
{
    internal static void Run(Action<bool, string> check)
    {
        CallbackUriRules(check);
        PipeDelivery(check);
    }

    private static void CallbackUriRules(Action<bool, string> check)
    {
        check(WindowsProtocolRegistration.IsCallbackUri(WindowsProtocolRegistration.CallbackUri),
            "标准协议回调 URI 被识别");
        check(WindowsProtocolRegistration.IsCallbackUri(WindowsProtocolRegistration.CallbackUri + "/"),
            "带尾斜杠的协议回调 URI 被识别");
        check(WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://auth/callback?code=abc&flow_id=def"),
            "带查询参数的协议回调 URI 被识别");
        check(WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://AUTH/callback"),
            "scheme 与 host 大小写不敏感");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.macos://auth/callback"),
            "其他平台 scheme 被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("https://auth/callback"),
            "https 回调被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://oauth/callback"),
            "非 auth host 被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://auth/other"),
            "非 callback 路径被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://auth/callback/..%2fother"),
            "路径穿越形式被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://user@auth/callback"),
            "带 userinfo 的回调被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://auth/callback#fragment"),
            "带 fragment 的回调被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("com.lexi.windows://auth:8080/callback"),
            "带显式端口的回调被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("auth/callback"),
            "相对路径被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri(""),
            "空字符串被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri("   "),
            "空白字符串被拒绝");
        check(!WindowsProtocolRegistration.IsCallbackUri(null),
            "null 被拒绝");
        var exact = WindowsProtocolRegistration.CallbackUri + "?x=" +
            new string('a', 4096 - WindowsProtocolRegistration.CallbackUri.Length - 3);
        check(exact.Length == 4096 && WindowsProtocolRegistration.IsCallbackUri(exact),
            "4096 字符上限内仍可识别");
        check(!WindowsProtocolRegistration.IsCallbackUri(exact + "a"),
            "超过 4096 字符被拒绝");
    }

    private static void PipeDelivery(Action<bool, string> check)
    {
        var previousDataDir = Environment.GetEnvironmentVariable("LEXI_DATA_DIR");
        var previousRegistration = Environment.GetEnvironmentVariable("LEXI_DISABLE_PROTOCOL_REGISTRATION");
        var dir = Path.Combine(Path.GetTempPath(), "lexi-protocol-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("LEXI_DATA_DIR", dir);
        Environment.SetEnvironmentVariable("LEXI_DISABLE_PROTOCOL_REGISTRATION", "1");
        try
        {
            using var primary = new SingleInstanceService();
            check(primary.CheckAndAcquire(wakeExisting: false), "测试主实例取得单实例锁");
            var pipeName = (string)typeof(SingleInstanceService)
                .GetField("PipeName", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            var callbacks = new List<Uri>();
            var callbackGate = new object();
            var wakeups = 0;
            var receiver = new Uri?[] { null };
            Action<Uri> handler = uri =>
            {
                lock (callbackGate) callbacks.Add(uri);
                receiver[0] = uri;
            };
            primary.WakeupRequested += () => Interlocked.Increment(ref wakeups);
            primary.ProtocolCallbackRequested += handler;

            SendRaw(pipeName, Encoding.UTF8.GetBytes("SHOW"));
            check(WaitUntil(() => Volatile.Read(ref wakeups) >= 1, 5000), "SHOW 唤醒信号送达主实例");

            var first = new Uri(WindowsProtocolRegistration.CallbackUri + "?code=one&flow_id=flow-one");
            SendRaw(pipeName, Frame(first.AbsoluteUri));
            check(WaitUntil(() => Volatile.Read(ref receiver[0]) is { } seen && Seen(seen, first), 5000),
                "长度前缀协议回调送达主实例");

            var exact = WindowsProtocolRegistration.CallbackUri + "?x=" +
                new string('a', 4096 - WindowsProtocolRegistration.CallbackUri.Length - 3);
            SendRaw(pipeName, Frame(exact));
            check(WaitUntil(() => Volatile.Read(ref receiver[0]) is { } seen && seen.AbsoluteUri == exact, 5000),
                "4096 字节上限回调仍被接受");

            var before = CallbackCount(callbacks, callbackGate);
            SendRaw(pipeName, Header(4097));
            SendRaw(pipeName, Frame("https://example.com/callback"));
            SendRaw(pipeName, Header(0));
            Thread.Sleep(400);
            check(CallbackCount(callbacks, callbackGate) == before,
                "超长、外部 scheme 与零长度帧全部被忽略");
            var survivor = new Uri(WindowsProtocolRegistration.CallbackUri + "?code=survivor&flow_id=flow-survivor");
            SendRaw(pipeName, Frame(survivor.AbsoluteUri));
            check(WaitUntil(() => Volatile.Read(ref receiver[0]) is { } seen && Seen(seen, survivor), 5000),
                "忽略非法帧后监听仍存活并继续投递");

            primary.ProtocolCallbackRequested -= handler;
            var pending = new Uri(WindowsProtocolRegistration.CallbackUri + "?code=pending&flow_id=flow-pending");
            SendRaw(pipeName, Frame(pending.AbsoluteUri));
            Thread.Sleep(500);
            check(CallbackCount(callbacks, callbackGate) == before + 1, "无订阅者时回调被缓存而非丢弃");
            primary.ProtocolCallbackRequested += handler;
            check(WaitUntil(() => Volatile.Read(ref receiver[0]) is { } seen && Seen(seen, pending), 2000),
                "待处理回调在订阅后立即投递");

            var wakeBefore = Volatile.Read(ref wakeups);
            var secondResult = Task.Run(() =>
            {
                using var second = new SingleInstanceService();
                return second.CheckAndAcquire(activationArgument: pending.AbsoluteUri);
            }).GetAwaiter().GetResult();
            check(!secondResult, "第二实例检测到主实例并退出");
            check(WaitUntil(() => Volatile.Read(ref wakeups) > wakeBefore, 5000),
                "第二实例通过管道唤醒主实例");

            var wakeMid = Volatile.Read(ref wakeups);
            var thirdResult = Task.Run(() =>
            {
                using var third = new SingleInstanceService();
                return third.CheckAndAcquire(activationArgument: null);
            }).GetAwaiter().GetResult();
            check(!thirdResult, "无回调参数的第三实例同样退出");
            check(WaitUntil(() => Volatile.Read(ref wakeups) > wakeMid, 5000),
                "无回调参数时发送 SHOW 唤醒");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEXI_DATA_DIR", previousDataDir);
            Environment.SetEnvironmentVariable("LEXI_DISABLE_PROTOCOL_REGISTRATION", previousRegistration);
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool Seen(Uri seen, Uri expected) =>
        string.Equals(seen.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal);

    private static int CallbackCount(List<Uri> callbacks, object gate)
    {
        lock (gate) return callbacks.Count;
    }

    private static bool WaitUntil(Func<bool> condition, int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    private static void SendRaw(string pipeName, byte[] payload)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        client.Connect(5000);
        client.Write(payload);
        client.Flush();
    }

    private static byte[] Header(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        return header;
    }

    private static byte[] Frame(string value)
    {
        var payload = Encoding.UTF8.GetBytes(value);
        var framed = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(framed, payload.Length);
        payload.CopyTo(framed, 4);
        return framed;
    }
}
