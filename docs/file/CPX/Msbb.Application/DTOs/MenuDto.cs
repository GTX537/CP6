using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Msbb.Application.DTOs
{
    // 用于表示前端所需的菜单项
    public class MenuDto
    {
        public int MajorCategoryNO { get; set; }
        public string MajorCategoryName { get; set; } = string.Empty;
        public int FunctionNO { get; set; }
        public string FunctionName { get; set; } = string.Empty;
        public string FunctionID { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
