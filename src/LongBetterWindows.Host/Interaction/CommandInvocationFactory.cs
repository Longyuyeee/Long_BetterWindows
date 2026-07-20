using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>为所有命令入口生成一致、最小化的结构化调用上下文。</summary>
    public static class CommandInvocationFactory
    {
        public static PluginCommandInvocation Create(
            CommandDescriptor descriptor,
            ContextSnapshot contextSnapshot)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(contextSnapshot);

            var context = contextSnapshot.SelectBest(descriptor.Command.AcceptedInputs);
            return new PluginCommandInvocation
            {
                CommandId = descriptor.Command.Id,
                InputType = context?.InputType ?? AcceptedInputType.None,
                Text = context?.Item.Text,
                Paths = context?.Item.Paths ?? Array.Empty<string>(),
                ImagePng = context?.InputType == AcceptedInputType.Image
                    ? context.Item.ImagePng
                    : null,
            };
        }
    }
}
