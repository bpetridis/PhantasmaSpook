﻿using Phantasma.Core.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Phantasma.Spook.GUI
{
    [Flags]
    public enum RedrawFlags
    {
        None = 0,
        Logo = 0x1,
        Prompt = 0x2,
        Content = 0x4
    }

    public delegate void ContentDisplay(int curY, int maxLines);

    public class ConsoleGUI: Logger
    {
        public struct LogEntry
        {
            public string Channel;
            public LogEntryKind Kind;
            public string Text;

            public LogEntry(string channel, LogEntryKind kind, string text)
            {
                this.Channel = channel;
                this.Kind = kind;
                this.Text = text;
            }
        }

        private byte[] logo;
        private ConsoleColor defaultBG;
        private List<LogEntry> _text = new List<LogEntry>();
        private RedrawFlags redrawFlags = RedrawFlags.None;
        private ContentDisplay currentDisplay;

        private bool ready = false;
        private bool initializing = true;
        private int animationCounter = 0;
        private DateTime lastRedraw;

        private static readonly string DefaultChannel = "main";
        private string currentChannel = DefaultChannel;
        private int _logIndex;

        private CommandDispatcher dispatcher;

        private string prompt = "";

        public ConsoleGUI()
        {
            Console.ResetColor();
            Console.Clear();
            this.defaultBG = Console.BackgroundColor;
            this.logo = Logo.GetPixels();
            this.redrawFlags = RedrawFlags.Logo | RedrawFlags.Prompt;

            this.currentDisplay = DisplayLog;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var colors = ColorMapper.GetBufferColors();
                colors[ConsoleColor.DarkCyan] = new COLORREF(52, 133, 157);
                colors[ConsoleColor.Cyan] = new COLORREF(126, 196, 193);
                colors[ConsoleColor.Yellow] = new COLORREF(245, 237, 186);
                colors[ConsoleColor.Red] = new COLORREF(210, 100, 103);
                colors[ConsoleColor.Green] = new COLORREF(192, 199, 65);
                colors[ConsoleColor.Blue] = new COLORREF(140, 143, 174);
                colors[ConsoleColor.DarkBlue] = new COLORREF(88, 69, 99);
                ColorMapper.SetBatchBufferColors(colors);
            }

            Update();
        }

        public void MakeReady(CommandDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            ready = true;
            initializing = false;
            redrawFlags |= RedrawFlags.Content | RedrawFlags.Logo | RedrawFlags.Prompt;
        }

        public override void Write(LogEntryKind kind, string msg)
        {
            WriteToChannel(DefaultChannel, kind, msg);
        }

        public void WriteToChannel(string channel, LogEntryKind kind, string input)
        {
            lock (_text)
            {
                var lines = input.Split('\n');
                foreach (var temp in lines)
                {
                    int maxWidth = Console.WindowWidth - 1;

                    var msg = temp;
                    while (msg != null)
                    {
                        string str;

                        if (msg.Length <= maxWidth)
                        {
                            str = msg;
                            msg = null;
                        }
                        else
                        {
                            str = msg.Substring(0, maxWidth);
                            msg = msg.Substring(maxWidth);
                        }

                        _text.Add(new LogEntry(channel, kind, str));
                    }
                }

                redrawFlags |= RedrawFlags.Content;
            }
        }

        private void FillLine(char symbol)
        {
            Console.Write(new string(symbol, (Console.WindowWidth - 1) - Console.CursorLeft));
            return;
        }

        private void Redraw()
        {
            //Console.Clear();
            Console.CursorVisible = false;

            int lY = 0;
            if (redrawFlags.HasFlag(RedrawFlags.Logo))
            {
                redrawFlags &= ~RedrawFlags.Logo;

                int midX = Console.WindowWidth / 2;
                int lX = midX - (Logo.Width / 2);

                Console.BackgroundColor = ConsoleColor.Black;

                for (int j = 0; j < Logo.Height; j++)
                {
                    Console.SetCursorPosition(lX, j + lY);
                    for (int i = 0; i < Logo.Width; i++)
                    {
                        var pixel = logo[i + j * Logo.Width];
                        if (pixel == 0)
                        {
                            Console.CursorLeft++;
                            continue;
                        }

                        switch (pixel)
                        {
                            case 1: Console.ForegroundColor = ConsoleColor.DarkCyan; break;
                            case 2: Console.ForegroundColor = ConsoleColor.Cyan; break;
                            case 3: Console.ForegroundColor = ConsoleColor.Yellow; break;
                        }

                        char ch;

                        if (j == Logo.Height-1)
                        {
                            ch = '▀';
                        }
                        else
                        if (j == 0)
                        {
                            ch = '▄';
                        }
                        else
                        {
                            ch = '█';
                        }
                        Console.Write(ch);
                    }
                }
            }

            Console.BackgroundColor = defaultBG;
            Console.ForegroundColor = ConsoleColor.DarkGray;

            if (redrawFlags.HasFlag(RedrawFlags.Prompt))
            {
                redrawFlags &= ~RedrawFlags.Prompt;

                Console.SetCursorPosition(0, Console.WindowHeight - 2);

                if (initializing)
                {
                    Console.Write("Booting Phantasma Spook");
                    int dots = animationCounter % 4;
                    for (int i = 0; i < dots; i++)
                    {
                        Console.Write(".");
                    }
                }                
                else
                if (currentDisplay == DisplayGraph)
                {
                    Console.Write("Press ESC to close graph...");
                }
                else
                {
                    Console.Write(">");

                    if (!string.IsNullOrEmpty(prompt))
                    {
                        Console.Write(prompt);
                    }

                    if (animationCounter % 2 == 0)
                    {
                        Console.Write("_");
                    }
                }

                FillLine(' ');
            }

            if (redrawFlags.HasFlag(RedrawFlags.Content))
            {
                redrawFlags &= ~RedrawFlags.Content;
                int curY = Logo.Height + lY;

                Console.SetCursorPosition(0, curY);
                FillLine('.');

                curY++;
                int maxLines = (Console.WindowHeight - 1) - (curY + 1);

                currentDisplay(curY, maxLines);
            }
        }

        private Dictionary<string, Graph> graphs = new Dictionary<string, Graph>();

        public void SetChannelGraph(string channel, Graph graph)
        {
            graphs[channel] = graph;
        }

        private void DisplayGraph(int curY, int maxLines)
        {
            var graph = graphs.ContainsKey(currentChannel) ? graphs[currentChannel] : null;
            if (graph == null)
            {
                Console.SetCursorPosition(0, curY);
                Console.Write($"No graph data available for '{currentChannel}'.");
                for (int j=1; j<maxLines; j++)
                {
                    Console.SetCursorPosition(0, curY +j);
                    FillLine(' ');
                }
                return;
            }
            
            int padLeft = graph.formatter(graph.maxPoint).Length + 1;

            int graphWidth = Console.WindowWidth - (padLeft + 1);

            int divisions = (int)(graph.maxPoint / (maxLines+1));
            if (divisions < 1)
            {
                divisions = 1;
            }

            for (int j=0; j<maxLines; j++)
            {
                int n = (maxLines-j) * divisions;
                Console.SetCursorPosition(0, curY + j);
                Console.Write(graph.formatter(n).PadRight(padLeft - 1));
                Console.Write('|');
            }

            int minPos = graph.data.Count - graphWidth;
            if (minPos < 0)
            {
                minPos = 0;
            }

            int offset = graphWidth > graph.data.Count ? graphWidth - graph.data.Count : 0;

            for (int i=0; i<graphWidth; i++)
            {
                int index = i + minPos - offset;
                int val = index >= 0 && index < graph.data.Count ? (int)graph.data[index] : 0;
                val /= divisions;

                if (val > maxLines)
                {
                    val = maxLines;
                }

                if (val < maxLines / 3)
                {
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                }
                else
                if (val < maxLines / 2)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                }
                else
                if (val < (maxLines / 3) * 2)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }

                for (int j=0; j<maxLines; j++)
                {
                    char ch;
                    if (j < val)
                    {
                        if (i > graphWidth / 2)
                        {
                            ch = '█';
                        }
                        else
                        if (i > graphWidth / 3)
                        {
                            ch = '▓';
                        }
                        else
                        if (i > graphWidth / 4)
                        {
                            ch = '▒';
                        }
                        else
                        {
                            ch = '░';
                        }
                    }
                    else
                    {
                        ch = ' ';
                    }

                    Console.SetCursorPosition(i + padLeft, curY + (maxLines - ( 1+ j)));
                    Console.Write(ch);
                }
           }

            redrawFlags |= RedrawFlags.Content;
        }

        private void DisplayLog(int curY, int maxLines)
        {
            int availableCount = _text.Sum(x => x.Channel == currentChannel ? 1 : 0);
            int maxIndex =  availableCount - maxLines;
            if (maxIndex < 0)
            {
                maxIndex = 0;
            }

            if (_logIndex > maxIndex)
            {
                _logIndex = maxIndex;
            }

            int srcIndex = _logIndex;
            int count = 0;

            while (count < maxLines)
            {
                LogEntry entry;

                if (srcIndex < _text.Count)
                {
                    entry = _text[srcIndex];
                    srcIndex++;
                    if (entry.Channel != currentChannel)
                    {
                        continue;
                    }
                }
                else
                {
                    entry = new LogEntry(DefaultChannel, LogEntryKind.Message, "");
                }

                Console.SetCursorPosition(0, curY + count);

                switch (entry.Kind)
                {
                    case LogEntryKind.Error: Console.ForegroundColor = ConsoleColor.Red; break;
                    case LogEntryKind.Warning: Console.ForegroundColor = ConsoleColor.Yellow; break;
                    case LogEntryKind.Sucess: Console.ForegroundColor = ConsoleColor.Green; break;
                    case LogEntryKind.Debug: Console.ForegroundColor = ConsoleColor.Cyan; break;
                    default: Console.ForegroundColor = ConsoleColor.Gray; break;
                }

                Console.Write(entry.Text);
                FillLine(' ');
                count++;
            }

            if (_logIndex < maxIndex)
            {
                _logIndex++;
                redrawFlags |= RedrawFlags.Content;

                if (_logIndex == maxIndex && ready)
                {
                    initializing = false;
                    ready = false;
                    redrawFlags |= RedrawFlags.Content | RedrawFlags.Logo | RedrawFlags.Prompt;
                }
            }
        }

        private void CheckKeys()
        {
            if (!Console.KeyAvailable)
            {
                return;
            }

            var press = Console.ReadKey();

            if (currentDisplay == DisplayGraph)
            {
                if (press.Key == ConsoleKey.Escape)
                {
                    SetChannel(currentChannel, DisplayLog);
                    redrawFlags |= RedrawFlags.Prompt | RedrawFlags.Content;
                }
                return;
            }

            if (press.KeyChar >= 32 && press.KeyChar <= 127)
            {
                prompt += press.KeyChar;
                redrawFlags |= RedrawFlags.Prompt;
            }
            else
            {
                switch (press.Key)
                {
                    case ConsoleKey.Enter:
                        {
                            if (!string.IsNullOrEmpty(prompt))
                            {
                                WriteToChannel(currentChannel, LogEntryKind.Message, prompt);
                                if (dispatcher != null)
                                {
                                    try
                                    {
                                        dispatcher.ExecuteCommand(prompt);
                                    }
                                    catch (CommandException e)
                                    {
                                        Write(LogEntryKind.Warning, e.Message);
                                    }
                                    catch (Exception e)
                                    {
                                        Write(LogEntryKind.Error, e.ToString());
                                    }
                                }
                                prompt = "";
                                redrawFlags |= RedrawFlags.Prompt;
                            }
                            break;
                        }

                    case ConsoleKey.Backspace:
                        {
                            if (!string.IsNullOrEmpty(prompt))
                            {
                                prompt = prompt.Substring(0, prompt.Length - 1);
                                redrawFlags |= RedrawFlags.Prompt;
                            }
                            break;
                        }
                }
            }
        }

        public void SetChannel(string channel, ContentDisplay display)
        {
            if (currentChannel == channel && display == currentDisplay)
            {
                return;
            }

            if (currentDisplay != display)
            {
                currentDisplay = display;
                redrawFlags |= RedrawFlags.Prompt;
            }

            if (currentChannel != channel)
            {
                currentChannel = channel;
                redrawFlags |= RedrawFlags.Content;
            }
        }

        public void SetChannel(string[] args, ContentDisplay display)
        {
            if (args.Length > 0)
            {
                SetChannel(args[0], display);
            }
            else
            {
                SetChannel(DefaultChannel, display);
            }
        }

        public void ShowLog(string[] args)
        {
            SetChannel(args, DisplayLog);
        }

        public void ShowGraph(string[] args)
        {
            SetChannel(args, DisplayGraph);
        }

        public void Update()
        {
            if (!initializing)
            {
                CheckKeys();
            }

            var diff = DateTime.UtcNow - lastRedraw;
            if (diff.TotalSeconds >= 1)
            {
                lastRedraw = DateTime.UtcNow;
                animationCounter++;
                redrawFlags |= RedrawFlags.Prompt;
            }

            if (redrawFlags != RedrawFlags.None) {
                lock (_text)
                {
                    Redraw();
                }
            }
        }
    }
}
