namespace Cmr.Api.Services;

/// <summary>
/// Drops EXIF, GPS, ICC and text chunks without a native image library.
/// Pixel re-encoding needs a decoder; the patched ImageSharp line now requires a paid license.
/// </summary>
public static class ImageSanitizer
{
    public static bool IsJpeg(byte[] raw) => raw.Length >= 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF;
    public static bool IsPng(byte[] raw) => raw.Length >= 8 && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47;

    public static byte[] StripJpeg(byte[] raw)
    {
        var output = new MemoryStream();
        output.Write(raw, 0, 2);
        var i = 2;
        while (i + 1 < raw.Length)
        {
            if (raw[i] != 0xFF)
                throw new Auth.AppException(400, "file", "तस्वीर अधूरी है। The image is incomplete.");
            while (i < raw.Length && raw[i] == 0xFF) i++;
            if (i >= raw.Length) break;
            var marker = raw[i++];
            if (marker == 0xD9) { output.WriteByte(0xFF); output.WriteByte(0xD9); break; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) { output.WriteByte(0xFF); output.WriteByte(marker); continue; }
            if (i + 1 >= raw.Length) break;
            var length = (raw[i] << 8) + raw[i + 1];
            if (length < 2 || i + length > raw.Length)
                throw new Auth.AppException(400, "file", "तस्वीर अधूरी है। The image is incomplete.");
            var drop = marker is >= 0xE1 and <= 0xEF;
            if (marker == 0xDA)
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                output.Write(raw, i, raw.Length - i);
                break;
            }
            if (!drop)
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                output.Write(raw, i, length);
            }
            i += length;
        }
        return output.ToArray();
    }

    public static byte[] StripPng(byte[] raw)
    {
        var output = new MemoryStream();
        output.Write(raw, 0, 8);
        var i = 8;
        while (i + 12 <= raw.Length)
        {
            var length = (raw[i] << 24) | (raw[i + 1] << 16) | (raw[i + 2] << 8) | raw[i + 3];
            if (length < 0 || i + 12 + length > raw.Length)
                throw new Auth.AppException(400, "file", "तस्वीर अधूरी है। The image is incomplete.");
            var type = System.Text.Encoding.ASCII.GetString(raw, i + 4, 4);
            var drop = type is "eXIf" or "tEXt" or "zTXt" or "iTXt" or "iCCP" or "tIME";
            if (!drop) output.Write(raw, i, 12 + length);
            i += 12 + length;
            if (type == "IEND") break;
        }
        return output.ToArray();
    }
}
