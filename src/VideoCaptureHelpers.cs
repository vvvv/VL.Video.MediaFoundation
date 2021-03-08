using SharpDX.MediaFoundation;
using SharpDX.Multimedia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VL.MediaFoundation
{
    public class VideoCaptureHelpers
    {
        public class Format
        {
            public int w, h;
            public float fr;
            public string format;
            public float aspect;
        }

        public static string GetSupportedFormats(int deviceIndex)
        {
            return String.Join(Environment.NewLine, EnumerateSupportedFormats(deviceIndex)
                .OrderByDescending(x => x.format)
                .ThenByDescending(x => x.w)
                .ThenByDescending(x => x.h)
                .ThenByDescending(x => x.fr)
                .Select(x => $"{x.format} {x.w}x{x.h} - {x.aspect:F2} @ {x.fr:F2}")
                .Distinct()
                .ToArray());
        }

        static IEnumerable<Format> EnumerateSupportedFormats(int deviceIndex)
        {
            var devices = EnumerateVideoDevices();
            var device = devices[deviceIndex];
            var name = device.Get(CaptureDeviceAttributeKeys.FriendlyName);
            var mediaSource = device.ActivateObject<MediaSource>();
            mediaSource.CreatePresentationDescriptor(out PresentationDescriptor descriptor);
            var streamDescriptor = descriptor.GetStreamDescriptorByIndex(0, out SharpDX.Mathematics.Interop.RawBool _);
            var handler = streamDescriptor.MediaTypeHandler;
            for (int i = 0; i < handler.MediaTypeCount; i++)
            {
                var mediaType = handler.GetMediaTypeByIndex(i);
                if (mediaType.MajorType == MediaTypeGuids.Video)
                {
                    var frameRate = ParseFrameRate(mediaType.Get(MediaTypeAttributeKeys.FrameRate));
                    ParseSize(mediaType.Get(MediaTypeAttributeKeys.FrameSize), out int width, out int height);
                    ParseSize(mediaType.Get(MediaTypeAttributeKeys.PixelAspectRatio), out int nominator, out int denominator);

                    var format = GetVideoFormat(mediaType);

                    yield return new Format() { w = width, h = height, fr = frameRate, format = format, aspect = nominator / denominator };
                }
            }
        }

        public static Activate[] EnumerateVideoDevices()
        {
            var attributes = new MediaAttributes();
            attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVideoCapture.Guid);
            return MediaFactory.EnumDeviceSources(attributes);
        }


        public static void ParseSize(long value, out int width, out int height)
        {
            width = (int)(value >> 32);
            height = (int)(value & 0x00000000FFFFFFFF);
        }

        public static long MakeSize(int width, int height)
        {
            return height + ((long)(width) << 32);
        }

        public static float ParseFrameRate(long value)
        {
            var numerator = (int)(value >> 32);
            var denominator = (int)(value & 0x00000000FFFFFFFF);
            return (float)(numerator * 100 / denominator) / 100f;
        }
        public static long MakeFrameRate(float value)
        {
            var r = 1 / value;
            var numerator = 100000 * value;
            var denominator = (int)(r * numerator);
            return denominator + ((long)(numerator) << 32);
        }

        private static string GetVideoFormat(MediaType mediaType)
        {
            // https://docs.microsoft.com/en-us/windows/desktop/medfound/video-subtype-guids
            var subTypeId = mediaType.Get(MediaTypeAttributeKeys.Subtype);
            var fourccEncoded = BitConverter.ToInt32(subTypeId.ToByteArray(), 0);
            var fourcc = new FourCC(fourccEncoded);
            return fourcc.ToString();
        }
    }
}
