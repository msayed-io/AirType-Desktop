using System.Windows.Media;
using System.Windows.Threading;
using LiveTypeBridge.App.Mvvm;

namespace LiveTypeBridge.App.ViewModels;

/// <summary>Data shown by the dedicated QR window: high-contrast code, big PIN, expiry countdown.</summary>
public sealed class QrViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private string _expiresText = "";

    public QrViewModel(ImageSource qrImage, string pin, string address, string sessionId, DateTime expiresUtc)
    {
        QrImage = qrImage;
        PinText = pin;
        AddressText = address;
        SessionIdText = sessionId;
        ExpiresUtc = expiresUtc;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public event Action? Expired;

    public ImageSource QrImage { get; }
    public string PinText { get; }
    public string AddressText { get; }
    public string SessionIdText { get; }
    public DateTime ExpiresUtc { get; }

    public string ExpiresText
    {
        get => _expiresText;
        private set => Set(ref _expiresText, value);
    }

    private void Tick()
    {
        var left = ExpiresUtc - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _timer.Stop();
            ExpiresText = "00:00";
            Expired?.Invoke();
        }
        else
        {
            ExpiresText = left.ToString(@"mm\:ss");
        }
    }

    public void Dispose() => _timer.Stop();
}
