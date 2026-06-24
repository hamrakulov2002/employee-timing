using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZKTECO_LIVE20R.Models;

namespace ZKTECO_LIVE20R.Services
{
    public interface IAttendanceRepository
    {
        Task<bool> RegisterEmployeeAsync(string fullName, string position, byte[] templateData);
        Task<List<FingerPrint>> GetAllFingerprintsAsync();
        Task<Employee?> VerifyAndLogAttendanceAsync(byte[] capturedTemplate, Func<byte[], byte[], bool> compareFunc);
    }
}