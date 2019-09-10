// Interface is for the classic "Command" design pattern from (GOF)

namespace BlueCats.Tools.Portable.Lang.CommandPattern
{
    public interface ICommandWithResult<out T> : ICommand {

        T GetResult();

    }
}