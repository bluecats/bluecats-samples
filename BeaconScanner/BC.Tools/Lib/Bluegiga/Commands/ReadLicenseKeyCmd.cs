using System.Threading.Tasks;

using BlueCats.Tools.Portable.IO;
using BlueCats.Tools.Portable.Lang.CommandPattern;

namespace BlueCats.Tools.Portable.Lib.Bluegiga.Commands {

    public class ReadLicenseKeyCmd : IAsyncCommandWithResult<byte[]> {

        public ReadLicenseKeyCmd( ISerialDevice serialDevice ) {
            _serialDevice = serialDevice;
        }

        private byte[] _licenseKey;
        private readonly ISerialDevice _serialDevice;

        public async Task<byte[]> ExecuteAsync() {

            using ( var api = new BGLibApi( _serialDevice ) ) {
                _licenseKey = new [] {
                    await api.RegReadAsync( 0xffc7 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffc8 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffc9 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffca ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffcb ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffcc ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffcd ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffce ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffcf ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd0 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd1 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd2 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd3 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd4 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd5 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd6 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd7 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd8 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffd9 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffda ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffdb ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffdc ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffdd ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffde ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffdf ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe0 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe1 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe2 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe3 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe4 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe5 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0xffe6 ).ConfigureAwait( false )
                };
            }

            return _licenseKey;
        }

        Task IAsyncCommand.ExecuteAsync() => ExecuteAsync();

    }

}