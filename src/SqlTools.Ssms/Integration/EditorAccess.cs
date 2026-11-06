using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using SqlTools.Infra;
using Task = System.Threading.Tasks.Task;

namespace SqlTools.Ssms.Integration
{
    /// <summary>Acesso ao editor moderno (IWpfTextView) e ao broker de completion.</summary>
    internal static class EditorAccess
    {
        public static IComponentModel ComponentModel
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            }
        }

        public static IWpfTextView GetActiveWpfTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var tm = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
                if (tm == null || tm.GetActiveView(1, null, out var vsView) != 0 || vsView == null) return null;
                return ComponentModel?.GetService<IVsEditorAdaptersFactoryService>()?.GetWpfTextView(vsView);
            }
            catch (Exception ex)
            {
                Log.Warn("GetActiveWpfTextView falhou.", ex);
                return null;
            }
        }

        /// <summary>
        /// Digita o texto tecla por tecla pela cadeia de comandos do editor (como o teclado faz), para que
        /// IntelliSense nativo e o nosso reajam de verdade. Devolve o texto final.
        /// </summary>
        public static async System.Threading.Tasks.Task<string> TypeAsync(IWpfTextView view, string text, int delayMs = 120)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            view.VisualElement.Focus();
            var target = ComponentModel.GetService<IVsEditorAdaptersFactoryService>().GetViewAdapter(view) as Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget;
            if (target == null) return null;
            var cmdGroup = Microsoft.VisualStudio.VSConstants.VSStd2K;
            foreach (var ch in text)
            {
                var pv = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(32);
                try
                {
                    System.Runtime.InteropServices.Marshal.GetNativeVariantForObject((ushort)ch, pv);
                    target.Exec(ref cmdGroup, (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.TYPECHAR, 0, pv, IntPtr.Zero);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pv);
                }
                await Task.Delay(delayMs);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }
            await Task.Delay(800);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return view.TextSnapshot.GetText();
        }

        public static async Task SendTabAsync(IWpfTextView view)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var target = ComponentModel.GetService<IVsEditorAdaptersFactoryService>().GetViewAdapter(view) as Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget;
            var cmdGroup = Microsoft.VisualStudio.VSConstants.VSStd2K;
            target?.Exec(ref cmdGroup, (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.TAB, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Abre o IntelliSense na posição do cursor (como Ctrl+Espaço) e devolve os textos dos itens.</summary>
        public static async System.Threading.Tasks.Task<List<string>> CollectCompletionItemsAsync(IWpfTextView view, int waitMs = 4000)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var broker = ComponentModel.GetService<IAsyncCompletionBroker>();
            broker.GetSession(view)?.Dismiss();
            var caret = view.Caret.Position.BufferPosition;
            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, view.TextSnapshot);
            var session = broker.TriggerCompletion(view, trigger, caret, CancellationToken.None);
            if (session == null) return new List<string>();
            session.OpenOrUpdate(trigger, caret, CancellationToken.None);

            var result = new List<string>();
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(300);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (session.IsDismissed) break;
                var computed = session.GetComputedItems(CancellationToken.None);
                var texts = computed?.Items?.Select(i => i.DisplayText).ToList();
                if (texts != null && texts.Count > 0)
                {
                    result = texts;
                    break;
                }
            }
            session.Dismiss();
            return result;
        }
    }
}
