using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class JobExecution : BaseEntity
{
    public string JobName { get; set; } = string.Empty;
    public DateTime DataExecucao { get; set; }
    public DateTime UltimaExecucao { get; set; }
}