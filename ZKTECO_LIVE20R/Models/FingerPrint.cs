using System;
using System.Collections.Generic;
using System.Text;

namespace ZKTECO_LIVE20R.Models;

public class FingerPrint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }

    public byte[] TemplateData { get; set; } = Array.Empty<byte>();

    public int FingerIndex { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    
    public virtual Employee Employee { get; set; } = null!;
}
