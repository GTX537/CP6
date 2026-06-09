using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 操作日志表 — 自动记录谁在什么时间做了什么
/// </summary>
public class Sys_OperLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// 操作人用户名
    /// </summary>
    [MaxLength(100)]
    public string? UserName { get; set; }

    /// <summary>
    /// 请求方法：GET/POST/PUT/DELETE
    /// </summary>
    [MaxLength(10)]
    public string? HttpMethod { get; set; }

    /// <summary>
    /// 请求路径，如 /api/article
    /// </summary>
    [MaxLength(500)]
    public string? RequestUrl { get; set; }

    /// <summary>
    /// Controller 名称
    /// </summary>
    [MaxLength(100)]
    public string? Controller { get; set; }

    /// <summary>
    /// Action 名称
    /// </summary>
    [MaxLength(100)]
    public string? Action { get; set; }

    /// <summary>
    /// 请求参数（Body JSON）
    /// </summary>
    public string? RequestBody { get; set; }

    /// <summary>
    /// 响应状态码
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// 客户端IP
    /// </summary>
    [MaxLength(50)]
    public string? ClientIp { get; set; }

    /// <summary>
    /// 操作时间
    /// </summary>
    public DateTime CreateDate { get; set; } = DateTime.Now;

    /// <summary>
    /// 告警标记（Phase 6）：true=需运维关注（如 IntegrationEvent DeadLetter）
    /// OperLog 查询 UI 可按此过滤
    /// </summary>
    public bool IsAlert { get; set; } = false;
}
