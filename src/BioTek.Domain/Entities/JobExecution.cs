using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class JobExecution : BaseEntity
{
    public string JobName { get; set; } = string.Empty;
    public DateTime DataExecucao { get; set; }
    public DateTime UltimaExecucao { get; set; }
    public List<UsuarioNotificado> UsuariosNotificados { get; set; } = new();
}

public class UsuarioNotificado
{
    public string Id { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
}