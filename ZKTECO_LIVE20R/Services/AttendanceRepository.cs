using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ZKTECO_LIVE20R.Data;
using ZKTECO_LIVE20R.Models;

namespace ZKTECO_LIVE20R.Services
{
    public class AttendanceRepository : IAttendanceRepository
    {
        // Регистрация сотрудника и его отпечатка в одной транзакции
        public async Task<bool> RegisterEmployeeAsync(string fullName, string position, byte[] templateData)
        {
            using var context = new AppDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var employee = new Employee
                {
                    Id = Guid.NewGuid(),
                    FullName = fullName,
                    Position = position
                };

                var fingerprint = new FingerPrint
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = employee.Id,
                    TemplateData = templateData
                };

                await context.Employees.AddAsync(employee);
                await context.FingerPrints.AddAsync(fingerprint);

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                return false;
            }
        }

        // Получить все шаблоны из Postgres для сравнения в памяти C#
        public async Task<List<FingerPrint>> GetAllFingerprintsAsync()
        {
            using var context = new AppDbContext();
            return await context.FingerPrints.Include(f => f.Employee).ToListAsync();
        }

        // Логика проверки отпечатка при прикладывании
        public async Task<Employee?> VerifyAndLogAttendanceAsync(byte[] capturedTemplate, Func<byte[], byte[], bool> compareFunc)
        {
            // 1. Извлекаем все зарегистрированные отпечатки из базы данных
            var allFingerprints = await GetAllFingerprintsAsync();

            // 2. Ищем совпадение в цикле через переданную функцию DBMatch из SDK
            var matchedFingerprint = allFingerprints.FirstOrDefault(f => compareFunc(f.TemplateData, capturedTemplate));

            if (matchedFingerprint == null) return null; // Такой отпечаток в базе не зарегистрирован

            using var context = new AppDbContext();

            // 3. Вычисляем тип события (Вход или Выход): 
            // Если сегодня записей для этого сотрудника еще не было — значит это Вход (Check-In)
            var today = DateTime.UtcNow.Date;
            bool isCheckIn = !await context.AttendanceLogs
                .AnyAsync(l => l.EmployeeId == matchedFingerprint.EmployeeId && l.Timestamp >= today);

            var log = new AttendanceLog
            {
                Id = Guid.NewGuid(),
                EmployeeId = matchedFingerprint.EmployeeId,
                Timestamp = DateTime.UtcNow,
                IsCheckIn = isCheckIn
            };

            await context.AttendanceLogs.AddAsync(log);
            await context.SaveChangesAsync();

            return matchedFingerprint.Employee;
        }
    }
}