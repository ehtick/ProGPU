using Thickness = Microsoft.UI.Xaml.Thickness;
using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Text;
using ProGPU.Compute;
using ProGPU.Virtualization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using Button = Microsoft.UI.Xaml.Controls.Button;
using StackPanel = Microsoft.UI.Xaml.Controls.StackPanel;

namespace ProGPU.Samples;

public static class DataVirtualizationPage
{
        private static DataGrid? s_dataGrid;
        private static float s_benchmarkScrollDirection = 1f;

        internal static void AdvanceBenchmarkScroll(float step)
        {
            if (s_dataGrid == null)
            {
                return;
            }

            float maxOffset = Math.Max(0f, s_dataGrid.TotalBodyHeight - s_dataGrid.ViewportHeight);
            if (maxOffset <= 0f)
            {
                return;
            }

            float nextOffset = s_dataGrid.ScrollOffset + s_benchmarkScrollDirection * step;
            if (nextOffset >= maxOffset)
            {
                nextOffset = maxOffset;
                s_benchmarkScrollDirection = -1f;
            }
            else if (nextOffset <= 0f)
            {
                nextOffset = 0f;
                s_benchmarkScrollDirection = 1f;
            }

            s_dataGrid.ScrollOffset = nextOffset;
        }

        public static FrameworkElement Create()
        {
            s_benchmarkScrollDirection = 1f;
            var grid = new Microsoft.UI.Xaml.Controls.Grid();
            grid.RowDefinitions.Add(new GridLength(70, GridUnitType.Absolute));   // Header
            grid.RowDefinitions.Add(new GridLength(1, GridUnitType.Star));       // Recycled Grid
    
            var descStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
            var listTitle = new RichTextBlock { Font = AppState._font, FontSize = 14f };
            listTitle.Inlines.Add(new Bold(new Run("10,000 Record Virtualized DataGrid")));
            descStack.AddChild(listTitle);
    
            var listDesc = new RichTextBlock { Font = AppState._font, FontSize = 11f, Margin = new Thickness(0, 2, 0, 0) };
            listDesc.Inlines.Add(new Run("Ultra-fast vertical scroll recycling displays massive datasets at locked 60 FPS. Click on any header column to "));
            listDesc.Inlines.Add(new Bold(new Run("sort alphanumerically")));
            listDesc.Inlines.Add(new Run(", and click rows to change selected indices. Double-click any cell (or press Enter on selection) to "));
            listDesc.Inlines.Add(new Bold(new Run("edit inline")) { Foreground = new SolidColorBrush(0x0078D4FF) });
            listDesc.Inlines.Add(new Run(". Press Enter to commit or Escape to cancel."));
            descStack.AddChild(listDesc);
    
            grid.AddChild(descStack);
            Microsoft.UI.Xaml.Controls.Grid.SetRow(descStack, 0);
    
            // Virtualized DataGrid setup
            var dataGrid = new Microsoft.UI.Xaml.Controls.DataGrid
            {
                Font = AppState._font,
                RowHeight = 28f,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(4)
            };
            s_dataGrid = dataGrid;
    
            // Define columns
            dataGrid.Columns.Add(new DataGridColumn("ID", 70f, "Id"));
            dataGrid.Columns.Add(new DataGridColumn("Activity Name", "*", "Name"));
            dataGrid.Columns.Add(new DataGridColumn("Status", "Auto", "Status"));
            dataGrid.Columns.Add(new DataGridColumn("Latency (ms)", 120f, "Latency"));
    
            // Setup direct, reflection-free binding for maximum speed
            dataGrid.CellValueBinding = (item, prop) =>
            {
                if (item is LogItem log)
                {
                    return prop switch
                    {
                        "Id" => log.Id.ToString(),
                        "Name" => log.Name,
                        "Status" => log.Status,
                        "Latency" => $"{log.Latency:F1}",
                        _ => string.Empty
                    };
                }
                return string.Empty;
            };
    
            // Populate logs
            AppState.EnsureLogItemsGenerated();
            foreach (var log in AppState._logItems)
            {
                dataGrid.AddItem(log);
            }
    
            grid.AddChild(dataGrid);
            Microsoft.UI.Xaml.Controls.Grid.SetRow(dataGrid, 1);
    
            return grid;
        }
}
