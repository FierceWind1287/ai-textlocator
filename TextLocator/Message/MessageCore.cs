using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TextLocator.Core;
using Rubyer;

namespace TextLocator.Message
{
    /// <summary>
    /// 消息盒子
    /// </summary>
    public class MessageCore
    {
        /// <summary>
        /// Rubyer.Message参数containerIdentifier
        /// </summary>
        private const string MESSAGE_CONTAINER = "MessageContainers";
        /// <summary>
        /// Rubyer.MessageBoxR参数containerIdentifier
        /// </summary>
        private const string MESSAGE_BOX_CONTAINER = "MessageBoxContainers";

        /// <summary>
        /// 警告
        /// </summary>
        /// <param name="message"></param>
        public static void ShowWarning(string message)
        {            
            void TryShow()
            {
                Rubyer.Message.Warning(MESSAGE_CONTAINER, message);
                //Rubyer.Message.Warning(message);
            }
            try
            {
                TryShow();
            }
            catch
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    TryShow();
                });
            }
        }

        /// <summary>
        /// 成功
        /// </summary>
        /// <param name="message"></param>
        public static void ShowSuccess(string message)
        {
            void TryShow()
            {
                Rubyer.Message.Success(MESSAGE_CONTAINER, message);
                //Rubyer.Message.Success(message);
            }
            try
            {
                TryShow();
            }
            catch
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    TryShow();
                });
            }
        }

        /// <summary>
        /// 错误
        /// </summary>
        /// <param name="message"></param>
        public static void ShowError(string message)
        {
            void TryShow()
            {
                Rubyer.Message.Error(MESSAGE_CONTAINER, message);
                //Rubyer.Message.Error(message);
            }
            try
            {
                TryShow();
            }
            catch
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    TryShow();
                });
            }
        }

        /// <summary>
        /// 信息
        /// </summary>
        /// <param name="message"></param>
        public static void ShowInfo(string message)
        {
            void TryShow()
            {
                Rubyer.Message.Info(MESSAGE_CONTAINER, message);
                //Rubyer.Message.Info(message);
            }
            try
            {
                TryShow();
            }
            catch
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    TryShow();
                });
            }
        }

        /// <summary>
        /// 确认提示
        /// </summary>
        /// <param name="message"></param>
        /// <param name="title"></param>
        /// <param name="button"></param>
        /// <returns></returns>
        public static Task<MessageBoxResult> ShowMessageBox(string message, string title, MessageBoxButton button = MessageBoxButton.OKCancel)
        {
            return Rubyer.MessageBoxR.Confirm(MESSAGE_BOX_CONTAINER, message, title, button);
        }
        //public static async Task<MessageBoxResult> ShowMessageBox(string message, string title, MessageBoxButton button = MessageBoxButton.OKCancel)
        //{
        //    return await Rubyer.MessageBoxR.Confirm(message, title, button);
        //}

    }
}
