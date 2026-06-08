using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Msbb.Domain.Entities
{
    [Table("T_SYS_MENU")] // 数据库表名
    public class SysMenu
    {
        // 复合主键1：大分类NO
        [Column(Order = 1)]
        public int MajorCategoryNO { get; set; }

        // 复合主键2：功能NO
        [Column(Order = 2)]
        public int FunctionNO { get; set; }

        [MaxLength(20)]
        public string MajorCategoryName { get; set; } = string.Empty; // 大分类名

        [MaxLength(20)]
        public string FunctionName { get; set; } = string.Empty;      // 功能名

        [MaxLength(20)]
        public string FunctionID { get; set; } = string.Empty;        // 功能ID (如 MSBBPA010)

        [MaxLength(200)]
        public string? Url { get; set; }                              // 路由地址

        public int DisplayOrder { get; set; }                         // 显示顺序

        // --- 共通审计字段 (确保它们在 HasData 中被正确处理) ---
        public bool DelFlg { get; set; } = false;

        [MaxLength(50)]
        public string InsUsrID { get; set; } = "SYSTEM"; // 修正：改为非空 string，并设置代码默认值
        public DateTime InsDate { get; set; } = DateTime.Now;

        [MaxLength(50)]
        public string? UpdUsrID { get; set; }
        public DateTime? UpdDate { get; set; }
    }
}