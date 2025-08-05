using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextLocator.Enums
{
    /// <summary>
    /// 排序类型
    /// </summary>
    public enum SortType
    {
        /// <summary>
        /// 默认排序
        /// </summary>
        Default = 0,
        /// <summary>
        /// 创建时间正序
        /// </summary>
        Date_ASC = 1,
        /// <summary>
        /// 创建时间倒叙
        /// </summary>
        Date_DESC = 2,
        /// <summary>
        /// 文件大小正序
        /// </summary>
        Size_ASC = 3,
        /// <summary>
        /// 文件大小倒叙
        /// </summary>
        Size_DESC = 4
    }
    public class SortOptionItem
    {
        public string DisplayName { get; set; }
        public SortType Value { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
