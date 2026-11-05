using System;
using QRCoder;

namespace vehicleManagement.BLL.Common
{
    public class QrCodeService
    {
        public byte[] GenerateQrCodeImage(string content, int pixelsPerModule = 20)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("二维码内容不能为空", nameof(content));

            using (var qrGenerator = new QRCodeGenerator())
            using (var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q))
            using (var qrCode = new PngByteQRCode(qrCodeData))
            {
                return qrCode.GetGraphic(pixelsPerModule);
            }
        }

        public string GenerateQrCodeBase64(string content, int pixelsPerModule = 20)
        {
            var imageBytes = GenerateQrCodeImage(content, pixelsPerModule);
            return $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
        }

        public string GenerateVehicleQrCodeUrl(string qrCodeToken, string baseUrl)
        {
            if (string.IsNullOrEmpty(qrCodeToken))
                throw new ArgumentException("二维码Token不能为空", nameof(qrCodeToken));

            var baseUri = baseUrl?.TrimEnd('/') ?? "";
            return $"{baseUri}/execute/{qrCodeToken}";
        }

        public string GenerateNewToken()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
