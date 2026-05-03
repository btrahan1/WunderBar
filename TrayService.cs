using System;
using System.Collections.Generic;
using System.Windows.Automation;
using System.Linq;

namespace WunderBar
{
    public class TrayIconInfo
    {
        public string Name { get; set; } = "";
    }

    public class TrayService
    {
        public List<TrayIconInfo> GetTrayIcons()
        {
            var results = new List<TrayIconInfo>();
            try
            {
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                
                var trayWnd = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
                
                if (trayWnd != null)
                {
                    foreach (AutomationElement btn in trayWnd.FindAll(TreeScope.Descendants, condition))
                    {
                        string name = btn.Current.Name;
                        // Only keep the "Show hidden icons" button
                        if (!string.IsNullOrEmpty(name) && name.ToLower().Contains("hidden icons"))
                        {
                            results.Add(new TrayIconInfo { Name = name });
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        private void AddResult(AutomationElement btn, List<TrayIconInfo> results)
        {
            // Not used
        }

        public void ClickTrayIcon(string name)
        {
            try
            {
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, name)
                );

                var btn = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition);
                if (btn != null)
                {
                    if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                    {
                        ((InvokePattern)pattern).Invoke();
                    }
                    else
                    {
                        // Fallback: use legacy IAccessible or just try to click center?
                        // For now, Invoke is best.
                    }
                }
            }
            catch { }
        }
    }
}
