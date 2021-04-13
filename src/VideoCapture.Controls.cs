using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reactive.Subjects;
using VL.Core;

namespace VL.MediaFoundation
{
    interface IControls<TName>
    {
        IEnumerable<Property<TName>> GetProperties();
    }

    public sealed class CameraControls : IControls<CameraControlPropertyName>
    {
        internal static readonly CameraControls Default = new CameraControls();

        internal CameraControls()
        {
            var values = Enum.GetValues(typeof(CameraControlPropertyName));
            Properties = values
                .Cast<CameraControlPropertyName>()
                .Select(v => new Property<CameraControlPropertyName>(v))
                .ToImmutableArray();
        }

        internal readonly ImmutableArray<Property<CameraControlPropertyName>> Properties;

        IEnumerable<Property<CameraControlPropertyName>> IControls<CameraControlPropertyName>.GetProperties() => Properties;
    }

    public sealed class VideoControls : IControls<VideoProcAmpProperty>
    {
        internal static readonly VideoControls Default = new VideoControls();

        internal VideoControls()
        {
            var values = Enum.GetValues(typeof(VideoProcAmpProperty));
            Properties = values
                .Cast<VideoProcAmpProperty>()
                .Select(v => new Property<VideoProcAmpProperty>(v))
                .ToImmutableArray();
        }

        internal readonly ImmutableArray<Property<VideoProcAmpProperty>> Properties;

        IEnumerable<Property<VideoProcAmpProperty>> IControls<VideoProcAmpProperty>.GetProperties() => Properties;
    }

    sealed class Property<TName> : IVLPin<float?>
    {
        public readonly TName Name;
        public readonly BehaviorSubject<float?> Subject;

        public Property(TName name)
        {
            Name = name;
            Subject = new BehaviorSubject<float?>(default);
        }

        public float? Value
        {
            get => Subject.Value;
            set
            {
                if (value != Value)
                    Subject.OnNext(value);
            }
        }

        object IVLPin.Value { get => Value; set => Value = (float?)value; }
    }
}
