using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using ZKTECO_LIVE20R.Data;

namespace ZKTECO_LIVE20R.Controllers;

public class AdminController : Controller
{
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;

        var employees = await _context.Employees
            .Include(e => e.FingerPrints)
            .Include(e => e.AttendanceLogs.Where(l => l.Timestamp >= today))
            .OrderBy(e => e.FullName)
            .ToListAsync();

        return View(employees);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        var employee = await _context.Employees
            .Include(e => e.FingerPrints)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee != null)
        {
            if (employee.FingerPrints != null && employee.FingerPrints.Any())
            {
                _context.FingerPrints.RemoveRange(employee.FingerPrints);
            }

            _context.Employees.Remove(employee);

            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }


    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
        {
            return NotFound();
        }
        return View(employee);
    }

    // POST: /Admin/Edit/{id}
    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, string fullName, string position)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            employee.FullName = fullName.Trim();
            employee.Position = string.IsNullOrWhiteSpace(position) ? "Сотрудник" : position.Trim();

            _context.Employees.Update(employee);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    // GET: /Admin/EmployeeReport/{id}
    public async Task<IActionResult> EmployeeReport(Guid id, int? year, int? month)
    {
        var employee = await _context.Employees
            .Include(e => e.FingerPrints)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            return NotFound();
        }

        int targetYear = year ?? DateTime.Now.Year;
        int targetMonth = month ?? DateTime.Now.Month;

        var startDate = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = startDate.AddMonths(1);

        var logs = await _context.AttendanceLogs
            .Where(l => l.EmployeeId == id && l.Timestamp >= startDate && l.Timestamp < endDate)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        ViewBag.Employee = employee;
        ViewBag.Year = targetYear;
        ViewBag.Month = targetMonth;

        return View(logs);
    }

    public async Task<IActionResult> MonthlyReport(int? year, int? month)
    {
        int targetYear = year ?? DateTime.Now.Year;
        int targetMonth = month ?? DateTime.Now.Month;

        var startDate = DateTime.SpecifyKind(new DateTime(targetYear, targetMonth, 1, 0, 0, 0), DateTimeKind.Utc);
        var endDate = startDate.AddMonths(1);

        // Загружаем всех сотрудников вместе с логами за этот месяц
        var employees = await _context.Employees
            .Include(e => e.AttendanceLogs.Where(l => l.Timestamp >= startDate && l.Timestamp < endDate))
            .OrderBy(e => e.FullName)
            .ToListAsync();

        // Формируем итоговый список с подсчетом часов и минут для каждого
        var reportList = new List<EmployeeMonthlyReportViewModel>();

        foreach (var emp in employees)
        {
            var sortedLogs = emp.AttendanceLogs.OrderBy(l => l.Timestamp).ToList();
            double totalHours = CalculateWorkedHours(sortedLogs);

            int hours = (int)totalHours;
            int minutes = (int)Math.Round((totalHours - hours) * 60);

            reportList.Add(new EmployeeMonthlyReportViewModel
            {
                FullName = emp.FullName,
                Position = emp.Position,
                TotalHours = hours,
                TotalMinutes = minutes,
                DaysWorkedCount = sortedLogs.Select(l => l.Timestamp.Date).Distinct().Count()
            });
        }

        ViewBag.Year = targetYear;
        ViewBag.Month = targetMonth;

        return View(reportList);
    }

    // Вспомогательный класс для отчета
    public class EmployeeMonthlyReportViewModel
    {
        public string FullName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public int TotalHours { get; set; }
        public int TotalMinutes { get; set; }
        public int DaysWorkedCount { get; set; }
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(string fullName, string position)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            var employee = new ZKTECO_LIVE20R.Models.Employee
            {
                Id = Guid.NewGuid(),
                FullName = fullName.Trim(),
                Position = string.IsNullOrWhiteSpace(position) ? "Сотрудник" : position.Trim()
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Payroll(Guid id, DateTime? startDate, DateTime? endDate, decimal hourlyRate = 250)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
        {
            return NotFound();
        }

        DateTime start;
        if (startDate.HasValue)
        {
            start = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
        }
        else
        {
            start = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek + (int)DayOfWeek.Monday);
        }

        DateTime end;
        if (endDate.HasValue)
        {
            end = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        }
        else
        {
            end = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
        }

        var logs = await _context.AttendanceLogs
            .Where(l => l.EmployeeId == id && l.Timestamp >= start && l.Timestamp <= end)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        double totalHours = CalculateWorkedHours(logs);
        decimal totalPay = (decimal)totalHours * hourlyRate;

        ViewBag.Employee = employee;
        ViewBag.StartDate = start.ToString("yyyy-MM-dd");
        ViewBag.EndDate = end.ToString("yyyy-MM-dd");
        ViewBag.HourlyRate = hourlyRate;
        ViewBag.TotalHours = Math.Round(totalHours, 2);
        ViewBag.TotalPay = Math.Round(totalPay, 2);

        return View(logs);
    }

    private double CalculateWorkedHours(List<ZKTECO_LIVE20R.Models.AttendanceLog> logs)
    {
        double totalHours = 0;
        DateTime? lastCheckIn = null;

        foreach (var log in logs)
        {
            if (log.IsCheckIn)
            {
                lastCheckIn = log.Timestamp;
            }
            else if (lastCheckIn.HasValue)
            {
                var span = log.Timestamp - lastCheckIn.Value;
                totalHours += span.TotalHours;
                lastCheckIn = null; 
            }
        }
        return totalHours;
    }

}
