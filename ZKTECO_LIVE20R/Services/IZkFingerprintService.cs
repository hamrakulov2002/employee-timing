using System;

namespace ZKTECO_LIVE20R.Services
{
    public interface IZkFingerprintService : IDisposable
    {
        bool Initialize();
        void CancelScan();

        // Событие срабатывает, когда сканер успешно считал палец
        event Action<byte[]>? FingerprintCaptured;

        // Событие для отправки текстовых статусов на экран
        event Action<string>? StatusChanged;
    }
}