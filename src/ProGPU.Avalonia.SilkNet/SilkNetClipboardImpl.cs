using System;
#if AVALONIA11
using System.Linq;
#endif
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Silk.NET.GLFW;

namespace Avalonia.SilkNet
{
    internal class SilkNetClipboardImpl : IClipboardImpl
    {
        private readonly Glfw _glfw = Glfw.GetApi();

        public unsafe Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            string text = _glfw.GetClipboardString(null);
            if (string.IsNullOrEmpty(text))
            {
                return Task.FromResult<IAsyncDataTransfer?>(null);
            }

            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            return Task.FromResult<IAsyncDataTransfer?>(dataTransfer);
        }

        public async Task SetDataAsync(IAsyncDataTransfer dataTransfer)
        {
            foreach (var item in dataTransfer.Items)
            {
                if (item.Contains(DataFormat.Text))
                {
                    var textObj = await item.TryGetRawAsync(DataFormat.Text);
                    if (textObj is string text)
                    {
                        SetClipboardText(text);
                        return;
                    }
                }
            }
        }

        private unsafe void SetClipboardText(string text)
        {
            _glfw.SetClipboardString(null, text);
        }

        public unsafe Task ClearAsync()
        {
            _glfw.SetClipboardString(null, string.Empty);
            return Task.CompletedTask;
        }
    }

    internal sealed class SilkNetClipboard : IClipboard
    {
        private readonly IClipboardImpl _clipboardImpl;

        public SilkNetClipboard(IClipboardImpl clipboardImpl)
        {
            _clipboardImpl = clipboardImpl;
        }

        public Task ClearAsync() => _clipboardImpl.ClearAsync();

        public Task SetDataAsync(IAsyncDataTransfer? dataTransfer) =>
            dataTransfer is null ? ClearAsync() : _clipboardImpl.SetDataAsync(dataTransfer);

        public Task FlushAsync() => Task.CompletedTask;

        public Task<IAsyncDataTransfer?> TryGetDataAsync() => _clipboardImpl.TryGetDataAsync();

        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() =>
            Task.FromResult<IAsyncDataTransfer?>(null);

#if AVALONIA11
#pragma warning disable CS0618
        public Task<string?> GetTextAsync() => ClipboardExtensions.TryGetTextAsync(this);

        public Task SetTextAsync(string? text) => ClipboardExtensions.SetTextAsync(this, text);

        public Task SetDataObjectAsync(IDataObject data)
        {
            if (data.Contains(DataFormats.Text) && data.Get(DataFormats.Text) is string text)
            {
                return SetTextAsync(text);
            }

            return ClearAsync();
        }

        public async Task<string[]> GetFormatsAsync()
        {
            var formats = await ClipboardExtensions.GetDataFormatsAsync(this);
            return formats.Select(format => format.Identifier).ToArray();
        }

        public Task<object?> GetDataAsync(string format) =>
            string.Equals(format, DataFormats.Text, StringComparison.Ordinal)
                ? GetTextObjectAsync()
                : Task.FromResult<object?>(null);

        private async Task<object?> GetTextObjectAsync() => await GetTextAsync();

        public Task<IDataObject?> TryGetInProcessDataObjectAsync() =>
            Task.FromResult<IDataObject?>(null);
#pragma warning restore CS0618
#endif
    }
}
