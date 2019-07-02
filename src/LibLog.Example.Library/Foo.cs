using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LibLog.Example.ColoredConsoleLogProvider, PublicKey=00240000048000009400000006020000002400005253413100040000010001006d4a2d52aa5943\r\n12957ed04795c617347a83c4202a6c9a5ea8510b6a70feea224be2a155c049aa38daca0e09902a\r\n313d2509e4a0a969654bd91ef3bccc69cf1876e612de31aecb30492e3e1e9970d896c5514ab565\r\n67525bda8785337f2d302fff17781955c13ab726381a7be3111e129174bec2c4472f94dc1ae441\r\n78376eb6")]

namespace LibLog.Example.Library
{
    using LibLog.Example.Library.Logging;

    public static class Foo
    {
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        public static void Bar()
        {
            Logger.Info("Baz");
        }
    }
}