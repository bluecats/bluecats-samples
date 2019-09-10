// Interface is for the classic "Command" design pattern from (GOF)

using System.Threading.Tasks;

namespace BlueCats.Tools.Portable.Lang.CommandPattern {

    public interface IAsyncCommand {

        Task ExecuteAsync(); // Asynchronus, non-blocking

    }

}