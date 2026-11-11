using System;
using System.Threading;
using System.Threading.Tasks;
using libzkfpcsharp; 

namespace ZKTECO_LIVE20R.Services
{
    // Данный сервис пришолсь просить помощи у ИИ тк это был мой первый опыт работы с C# и SDK ZKTeco. Я не знал как правильно реализовать захват отпечатка и его сравнение, поэтому я обратился к ИИ за помощью.
    // ИИ помог мне написать код, который инициализирует сканер, захватывает отпечаток и сравнивает его с зарегистрированными шаблонами.
    // Я благодарен ИИ за помощь и надеюсь, что этот код будет полезен другим разработчикам, которые сталкиваются с подобной задачей.
    public class ZkLive20RService : IZkFingerprintService
    {
        private IntPtr _deviceHandle = IntPtr.Zero;
        private IntPtr _dbCacheHandle = IntPtr.Zero; // Хэндл кэша памяти биометрии SDK
        private bool _isScanning = false;
        private CancellationTokenSource? _cts;

        public event Action<byte[]>? FingerprintCaptured;
        public event Action<string>? StatusChanged;

        public bool Initialize()
        {
            int initResult = zkfp2.Init();
            if (initResult != zkfp.ZKFP_ERR_OK)
            {
                StatusChanged?.Invoke($"Ошибка инициализации SDK. Код: {initResult}");
                return false;
            }

            _deviceHandle = zkfp2.OpenDevice(0);
            if (_deviceHandle == IntPtr.Zero)
            {
                StatusChanged?.Invoke("Сканер Live20R не найден. Проверьте USB-подключение.");
                zkfp2.Terminate();
                return false;
            }

            _dbCacheHandle = zkfp2.DBInit();
            if (_dbCacheHandle == IntPtr.Zero)
            {
                StatusChanged?.Invoke("Ошибка инициализации кэша биометрии.");
                zkfp2.CloseDevice(_deviceHandle);
                zkfp2.Terminate();
                return false;
            }

            StatusChanged?.Invoke("Сканер успешно инициализирован. Ожидание пальца...");
            StartCaptureLoop();
            return true;
        }

        public bool MatchTemplates(byte[] regTemplate, byte[] shotTemplate)
        {
            if (_dbCacheHandle == IntPtr.Zero) return false;

            int score = zkfp2.DBMatch(_dbCacheHandle, regTemplate, shotTemplate);
            return score >= 80;
        }

        private void StartCaptureLoop()
        {
            _isScanning = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(() =>
            {
                byte[] templateBuffer = new byte[2048]; // Буфер для шаблона отпечатка
                byte[] imageBuffer = new byte[640 * 480]; // Буфер для картинки (при необходимости)

                while (!token.IsCancellationRequested)
                {
                    int templateSize = templateBuffer.Length;

                    int result = zkfp2.AcquireFingerprint(_deviceHandle, imageBuffer, templateBuffer, ref templateSize);

                    if (result == zkfp.ZKFP_ERR_OK)
                    {
                        byte[] cleanTemplate = new byte[templateSize];
                        Buffer.BlockCopy(templateBuffer, 0, cleanTemplate, 0, templateSize);

                        try { FingerprintCaptured?.Invoke(cleanTemplate); }
                        catch (Exception ex) { StatusChanged?.Invoke("Ошибка обработчика: " + ex.Message); }
                    }

                    Thread.Sleep(150);
                }
            }, token);
        }

        public void SimulateCapture(byte[]? customTemplate = null)
        {
            // Если шаблон не передан, создаем фейковый массив (размером как стандартный шаблон ZKTeco)
            byte[] templateToInject = customTemplate ?? new byte[512];
            if (customTemplate == null)
            {
                new Random().NextBytes(templateToInject);
            }

            StatusChanged?.Invoke("Эмуляция сканирования отпечатка...");

            try
            {
                // Напрямую вызываем событие, как будто сканер только что поймал палец
                FingerprintCaptured?.Invoke(templateToInject);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Ошибка эмулятора: " + ex.Message);
            }
        }

        public void CancelScan()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            CancelScan();

            if (_deviceHandle != IntPtr.Zero)
            {
                zkfp2.CloseDevice(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
            if (_dbCacheHandle != IntPtr.Zero)
            {
                zkfp2.DBFree(_dbCacheHandle);
                _dbCacheHandle = IntPtr.Zero;
            }
            if (_isScanning)
            {
                zkfp2.Terminate(); // ИСПРАВЛЕНО: Вместо Finalize
                _isScanning = false;
            }
        }
    }
}