using System;
using System.Collections.Specialized;
using System.IO;
using AcTools.Utils.Helpers;
using CefSharp;
using FirstFloor.ModernUI.Helpers;
using JetBrains.Annotations;

namespace AcManager.Controls.UserControls.CefSharp {
    public class AltFilesHandlerFactory : ISchemeHandlerFactory {
        public const string SchemeName = "file";

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request) {
            if (schemeName == SchemeName) {
                try {
                    var slice = SchemeName.Length + 4;
                    if (slice >= request.Url.Length) return null;
                    var filename = $@"{request.Url[slice - 1].ToInvariantString()}:{request.Url.Substring(slice)}";
                    return new CustomResourceHandler(filename);
                } catch (Exception e) {
                    Logging.Error(e);
                }
            }

            return null;
        }

        private class CustomResourceHandler : IResourceHandler {
            [CanBeNull]
            private readonly Stream _data;

            private readonly string _mimeType;

            public CustomResourceHandler(string filename) {
                try {
                    _data = File.Exists(filename) ? File.OpenRead(filename) : null;
                } catch (Exception e) {
                    Logging.Warning(e);
                    _data = null;
                }

                try {
                    _mimeType = ResourceHandler.GetMimeType(Path.GetExtension(filename));
                } catch (Exception e) {
                    Logging.Warning(e);
                    _mimeType = "application/octet-stream";
                }
            }

            bool IResourceHandler.ProcessRequest(IRequest request, ICallback callback) {
                callback.Continue();
                return true;
            }

            void IResourceHandler.GetResponseHeaders(IResponse response, out long responseLength, out string redirectUrl) {
                redirectUrl = null;
                if (_data == null) {
                    responseLength = 0L;
                    response.ErrorCode = CefErrorCode.FileNotFound;
                } else {
                    response.MimeType = _mimeType;
                    response.StatusCode = 200;
                    response.StatusText = "OK";
                    response.ResponseHeaders = new NameValueCollection();
                    responseLength = _data.Length;
                }
            }

            bool IResourceHandler.ReadResponse(Stream dataOut, out int bytesRead, ICallback callback) {
                if (!callback.IsDisposed) {
                    callback.Dispose();
                }

                if (_data == null) {
                    bytesRead = 0;
                    return false;
                }

                bytesRead = _data.CopyTo(dataOut, (int)dataOut.Length, 8192);
                return bytesRead > 0;
            }

            bool IResourceHandler.CanGetCookie(Cookie cookie) => true;
            bool IResourceHandler.CanSetCookie(Cookie cookie) => true;

            void IDisposable.Dispose() {
                _data?.Dispose();
            }

            void IResourceHandler.Cancel() { }
        }
    }
}