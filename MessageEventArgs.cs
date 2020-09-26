using System;

namespace VellumZero
{
    public class MessageEventArgs : EventArgs
    {
        public string Server { get; private set; }
        public string User { get; private set; }
        public string Text { get; private set; }

        public MessageEventArgs(string s, string u, string t)
        {
            Server = s;
            User = u;
            Text = t;
        }
    }
}