namespace TextLocator.Enums
{
    /// <summary>
    /// 搜索域
    /// </summary>
    public enum SearchRegion
    {
        /// <summary>
        /// 文件名和内容
        /// </summary>
        FileNameAndContent,
        /// <summary>
        /// 仅文件名
        /// </summary>
        FileNameOnly,
        /// <summary>
        /// 进文件内容
        /// </summary>
        ContentOnly

    }
    public class SearchRegionItem
    {
        public string DisplayName { get; set; }      // 显示的文本
        public SearchRegion Value { get; set; }      // 枚举值

        public override string ToString()
        {
            return DisplayName; // 用于 ComboBox 显示
        }
    }

}
