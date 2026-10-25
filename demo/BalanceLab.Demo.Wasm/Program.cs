using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using BalanceLab.Demo;

// Start runtime'u nic nie robi: serwer powstaje dopiero na Start() z workera, ktory ma juz gotowy odbiornik zmian.
return;

/// <summary>
/// Metody dla workera w przegladarce. Jeden serwer na karte; zmiany z pokoi wracaja przez funkcje <c>emit</c>
/// modulu "demo", ktora worker rejestruje przed startem.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class DemoInterop
{
    private static DemoBridge? _bridge;

    private static DemoBridge Bridge => _bridge ?? throw new InvalidOperationException("Demo nie wystartowalo - najpierw Start().");

    /// <param name="role">"game" — lobby i stoly (z pokazowymi partiami w tle), "lab" — laboratorium w osobnym workerze.</param>
    [JSExport]
    internal static void Start(string role, int thinkingMs)
    {
        if (_bridge is not null)
        {
            return;
        }

        _bridge = new DemoBridge(Emit);
        if (role == "lab")
        {
            _bridge.SubmitShowcaseExperiment();
        }
        else
        {
            // Pokaz w tle: worker odpowiada na wywolania, a partie pokazowe ida miedzy nimi.
            _ = _bridge.PlayShowcaseAsync(TimeSpan.FromMilliseconds(thinkingMs));
        }
    }

    [JSExport]
    internal static string Connect() => Bridge.Connect();

    [JSExport]
    internal static Task<string> Invoke(string connection, string method, string arguments) => Bridge.InvokeAsync(connection, method, arguments);

    [JSExport]
    internal static string Fetch(string method, string path, string? body) => Bridge.Fetch(method, path, body);

    [JSImport("emit", "demo")]
    private static partial void Emit(string connection, string kind, string json);
}
