// Interface is for the classic "Command" design pattern from (GOF)

using System.Threading.Tasks;

namespace BlueCats.Tools.Portable.Lang.CommandPattern {

    public interface IAsyncCommandWithResult<T> : IAsyncCommand {

        new Task<T> ExecuteAsync(); // Asynchronus, non-blocking

    }

}