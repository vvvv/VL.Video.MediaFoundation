using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System.Collections.Immutable;
using System.Linq;
using VL.Core;
using VL.Core.CompilerServices;
using VL.Lib.Basics.Resources;

[assembly: AssemblyInitializer(typeof(VL.MediaFoundation.Initialization))]

namespace VL.MediaFoundation
{
    public sealed class Initialization : AssemblyInitializer<Initialization>
    {
        protected override void RegisterServices(IVLFactory factory)
        {
            if (!factory.HasService<NodeContext, IResourceProvider<Device>>())
            {
                factory.RegisterService<NodeContext, IResourceProvider<Device>>(nodeContext =>
                {
                    // One per entry point
                    return ResourceProvider.NewPooledPerApp(nodeContext, () => new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport));
                });
            }
            factory.RegisterNodeFactory("VL.Video.MediaFoundation.ControlNodes", f =>
            {
                // TODO: fragmented = true causes troubles on disconnect, apparently the default value injection can't deal with nullables -> injects 0 instead of null
                var nodes = ImmutableArray.Create(
                    f.NewNodeDescription(nameof(CameraControls), "Video.MediaFoundation.Advanced", fragmented: false, bc =>
                    {
                        return bc.NewNode(
                            inputs: CameraControls.Default.Properties.Select(p => Pin(bc, p.Name.ToString())),
                            outputs: new[] { bc.Pin("Output", typeof(CameraControls)) },
                            newNode: ibc =>
                            {
                                var controls = new CameraControls();
                                return ibc.Node(
                                    inputs: controls.Properties,
                                    outputs: new[] { ibc.Output(() => controls) });
                            });
                    }),
                    f.NewNodeDescription(nameof(VideoControls), "Video.MediaFoundation.Advanced", fragmented: false, bc =>
                    {
                        return bc.NewNode(
                            inputs: VideoControls.Default.Properties.Select(p => Pin(bc, p.Name.ToString())),
                            outputs: new[] { bc.Pin("Output", typeof(VideoControls)) },
                            newNode: ibc =>
                            {
                                var controls = new VideoControls();
                                return ibc.Node(
                                    inputs: controls.Properties,
                                    outputs: new[] { ibc.Output(() => controls) });
                            });
                    }));
                return NodeBuilding.NewFactoryImpl(nodes);
            });
        }

        static IVLPinDescription Pin(NodeBuilding.NodeDescriptionBuildContext bc, string name) => bc.Pin(name, typeof(Optional<float>));
    }
}
