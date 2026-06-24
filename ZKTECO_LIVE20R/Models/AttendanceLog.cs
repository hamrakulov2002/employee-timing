using System;
using System.Collections.Generic;
using System.Text;

namespace ZKTECO_LIVE20R.Models;

public class AttendanceLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public bool IsCheckIn { get; set; }

    public virtual Employee Employee { get; set; } = null!;

}
