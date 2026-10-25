// Runs the C# bridge (no UI) and prints what the add-on sends, for browser end-to-end tests.
using System; using System.Linq; using System.Threading; using NexGet;
static class BridgeHost {
    static void Main(string[] a) {
        var b = new Bridge(new Config());
        b.Request += (k, d) => {
            if (k == "grab") { Choice c; var an = Video.Find(d.Str("pageUrl"), d.Str("id"), out c);
                Console.WriteLine("GRAB " + (c == null ? "not found" : c.Label + " | headers: " + string.Join(", ", c.Headers.Where(h => h.Key != "User-Agent").Select(h => h.Key + "=" + h.Value)))); }
            else Console.WriteLine("EVENT " + k + " " + d.Str("pageUrl", d.Str("url")));
            Console.Out.Flush();
        };
        Console.WriteLine("ready"); Console.Out.Flush();
        Thread.Sleep(int.Parse(a[0]) * 1000);
        Console.WriteLine("pinged by chrome: " + b.SeenFrom("chrome"));
    }
}
