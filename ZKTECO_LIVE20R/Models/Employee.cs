using System;
using System.Collections.Generic;
using System.Text;

namespace ZKTECO_LIVE20R.Models;

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<FingerPrint> FingerPrints { get; set; } = new List<FingerPrint>();
    public virtual ICollection<AttendanceLog> AttendanceLogs { get; set; } = new List<AttendanceLog>();
}
