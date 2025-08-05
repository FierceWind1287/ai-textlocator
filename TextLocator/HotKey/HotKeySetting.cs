using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextLocator.HotKey
{
    /// <summary>
    /// 快捷键设置项枚举, Description为默认键
    /// </summary>
    public enum HotKeySetting
    {
        /// <summary>
        /// 显示
        /// </summary>
        [Description("D")]
        Show = 0,
        /// <summary>
        /// 隐藏
        /// </summary>
        [Description("H")]
        Hide = 1,
        /// <summary>
        /// 清空
        /// </summary>
        [Description("C")]
        Clear = 2,
        /// <summary>
        /// 退出
        /// </summary>
        [Description("E")]
        Exit = 3,
        /// <summary>
        /// 上一个
        /// </summary>
        [Description("Left")]
        Previous = 4,
        /// <summary>
        /// 下一个
        /// </summary>
        [Description("Right")] 
        Next = 5
    }
}
