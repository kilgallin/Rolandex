#define debug

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Midi;
using System.Diagnostics;
using WMPLib;
using System.Net;

namespace ConsoleApplication2
{
    class Program
    {
        // For managing pedals differently in different program states
        enum Mode { menu, music, test };
        static Mode mode;
        
        // Maps entries on the menu to corresponding sheet music
        static Dictionary<int, string> fileMap;

        // Tracks the selection the user has currently made in the menu
        static int menuSelection = 0;

        // Tracks which pedals are pressed
        static bool pedal1 = false;
        static bool pedal2 = false;

        // Used to prevent duplicate inputs on pedal release
        static int skipControl = 0;

        // Midi connections
        static OutputDevice pianOut;
        static InputDevice piano;
        static Instrument[] instruments;
        static int instrumentIndex = 0;

        //Playing sounds
        static WMPLib.WindowsMediaPlayer player;

        // State management
        static List<autoState> automaton;
        static int currentAutoState = 0;

        // Represents a state in an automaton with outbound transitions
        class autoState
        {
            public enum action { play, up, down, page, chord, exit, none }
            public int p1State;
            public action p1Action;
            public string p1Arg;
            public int p2State;
            public action p2Action;
            public string p2Arg;
            public int p12State;
            public action p12Action;
            public string p12Arg;
            public Dictionary<Pitch, int> keyStates;
            public Dictionary<Pitch, action> keyActions;
            public Dictionary<Pitch, string> keyArgs;

        }

        static void initialize()
        {
            //Start on the menu for launching sheet music or stage control files
            mode = Mode.menu;

            player = new WMPLib.WindowsMediaPlayer();

            //For outputting to connected keyboard
            instruments = new Instrument[] { Instrument.AcousticGrandPiano, Instrument.AltoSax, Instrument.ElectricPiano1, Instrument.Fiddle };
        }

        static void loadMusic()
        {
            //Get the sheet music pdfs from the "music" directory, put them in dictionary with their index
            string[] files = Directory.GetFiles("music", "*.pdf", SearchOption.AllDirectories);
            fileMap = new Dictionary<int, string>();
            for (int i = 0; i < files.Length; i++)
            {
                fileMap.Add(i, files[i]);
            }
            //Add the stage control files to the list
            files = Directory.GetFiles("data", "*.jda", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                fileMap.Add(fileMap.Count, files[i]);
            }
        }

        static void displayMenu()
        {
            for (int i = 0; i < fileMap.Count; i++)
            {
                Console.WriteLine(i + " " + fileMap[i]);
            }
            Console.WriteLine("Use function pedals to select an item and sustain pedal to confirm. Press enter to exit.");
        }

        static void launchPiano()
        {
            //Connect to the keyboard
            if (InputDevice.InstalledDevices.Count > 0)
            {
                pianOut = Midi.OutputDevice.InstalledDevices[1];
                piano = InputDevice.InstalledDevices[0];
                Console.WriteLine(piano.Name);
                Console.WriteLine(pianOut.Name);
                piano.ControlChange += piano_ControlChange;
                piano.Open();
                piano.StartReceiving(new Clock(120));
                pianOut.Open();
                piano.ProgramChange += piano_ProgramChange;
                //piano.NoteOn += piano_NoteOn;
                //pianOut.SendProgramChange(Channel.Channel1, Instrument.Accordion);
            }
            else
            {
                Console.WriteLine("No devices connected");
            }
        }

        static void closePiano()
        {
            if(piano != null){
                piano.StopReceiving();
                piano.Close();
            }
        }

        static void Main(string[] args)
        {
            initialize();
            loadMusic();
            displayMenu();
            launchPiano();
            Console.ReadLine();
            closePiano();
        }

        static void piano_NoteOn(NoteOnMessage msg)
        {
            Pitch p = msg.Pitch;
            Console.WriteLine(p.ToString());
                                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://kilgallin.com/write?note="+p.ToString());
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

                   
                    Stream stream = request.GetRequestStream();
                    stream.Close();

                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        Console.WriteLine(reader.ReadToEnd());
                    }  
                    


            if (mode == Mode.menu)
            {
                if (p == Pitch.B3)
                {
                    menuBackward();
                }
                else if (p == Pitch.D4)
                {
                    menuForward();
                }
                else if (p == Pitch.C4)
                {
                    menuSelect();
                }
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(menuSelection + "  ");
            }
        }

        static void piano_ProgramChange(ProgramChangeMessage msg)
        {
            Console.WriteLine(msg.ToString());
        }

        static void menuForward()
        {
            menuSelection = (menuSelection + 1) % fileMap.Count;
        }

        static void menuBackward()
        {
            menuSelection--;
        }

        static void menuSelect()
        {
            //get to test mode by selecting a negative piece
            if (menuSelection < 0)
            {
                mode = Mode.test;
            }
            else if (menuSelection < fileMap.Count)
            {
                //If the piece came from an automaton datafile, load its contents and start
                //processing control changes on it
                if (fileMap[menuSelection].Contains("jda"))
                {
                    mode = Mode.music;
                }
                //if it came from a pdf, open the pdf and start paging with the pedals
                else
                {
                    Process.Start("openMusic.exe", "\"" + fileMap[menuSelection] + "\"");
                    mode = Mode.music;
                }
            }
        }



        static void piano_ControlChange(ControlChangeMessage msg)
        {
            Console.WriteLine(msg.ToString());


            //The pedal that was pressed. 1=FC1, 2=FC2, 3=Sustain, negatives=corresponding pedalup
            int pedal = 0;

            /* RD-700NX instrument switching sends out messages on all channels. 
             * Look for them and discard the channel1s that spoof the pedals */
            if (msg.Control.ToString() == "67" && msg.Value == 0 && msg.Channel == Channel.Channel4) skipControl = 3;
            if (skipControl > 0 && msg.Channel == Channel.Channel1)
            {
                skipControl--;
                return;
            }

            //Identify the pedal based on the control message
            if (msg.Control.ToString() == "67" && msg.Value == 0 && msg.Channel == Channel.Channel1)
            {
                pedal = 1;
                pedal1 = true;
            }
            else if (msg.Control.ToString() == "66" && msg.Value == 0 && msg.Channel == Channel.Channel1)
            {
                pedal = 2;
                pedal2 = true;
            }
            else if (msg.Control.ToString() == "67" && msg.Value == 127 && msg.Channel == Channel.Channel1)
            {
                pedal = -1;
                pedal1 = false;
            }
            else if (msg.Control.ToString() == "66" && msg.Value == 127 && msg.Channel == Channel.Channel1)
            {
                pedal = -2;
                pedal2 = false;
            }
            else if (msg.Control.ToString() == "SustainPedal" && msg.Value == 127 && msg.Channel == Channel.Channel1)
            {
                pedal = 3;
            }
            else return;

            //large switching depending on context
            switch (mode)
            {
                //Test is to examine device configuration and connectivity. Only enable output if debug mode is on
                case Mode.test:
                    Console.WriteLine(msg.Control.ToString() + " " + msg.Control.Name() + " " + msg.Channel.Name() + " " + msg.Channel + " " + msg.Value + " " + msg.Device.Name + " " + msg.Device.ToString() + " " + pedal);
                    break;
                //In the menu, we move the selection up and down the list, or move to another mode
                case Mode.menu:
                    if (pedal == 1)
                    {
                        menuBackward();
                    }
                    else if (pedal == 2)
                    {
                        menuForward();
                    }
                    //sustain pedal
                    else if (pedal == 3)
                    {
                        menuSelect();
                    }
                    //write the currently selected piece number on the current line
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write(menuSelection + "  ");
                    break;
                case Mode.music:
                    if (pedal == 1)
                    {
                        Process.Start("pedal1.exe");
                    }
                    else if (pedal == 2)
                    {
                        Process.Start("pedal2.exe");
                    }
                    //sustain pedal
                    /*
                    else if (pedal == 3)
                    {
                    
                        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://kilgallin.com:81/?key=");
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*;q=0.8";

                   
                    Stream stream = request.GetRequestStream();
                    stream.Close();

                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        Console.WriteLine(reader.ReadToEnd());
                    }  
                                }
                    */            break;
                    
                        }
                    }
      
                    //TODO: scrap all this
                    /*if (pedal1 && pedal2)
                    {
                        if (automaton == null)
                        {
                            //Process.Start("closeMusic.exe");
                            //mode = Mode.menu;
                        }
                        else
                        {
                            autoState state = automaton[currentAutoState];
                            processTransition(state.p12State, state.p12Action, state.p12Arg);
                        }
                    }
                    else if (pedal == 1)
                    {
                        if (automaton == null)
                        {
                            Process.Start("pedal1.exe");
                        }
                        else
                        {
                            autoState state = automaton[currentAutoState];
                            processTransition(state.p1State, state.p1Action, state.p1Arg);
                        }
                    }
                    else if (pedal == 2)
                    {
                        if (automaton == null)
                        {
                            Process.Start("pedal2.exe");
                        }
                        else
                        {
                            autoState state = automaton[currentAutoState];
                            processTransition(state.p2State, state.p2Action, state.p2Arg);
                        }
                    }
                    break;
            }
        }

        static void loadAutomaton(string filename)
        {
            StreamReader tr = new StreamReader(filename);
            // This can't belong...
            //Process.Start(tr.ReadLine(), "\"" + filename + "\"");
            string[] size = tr.ReadLine().Split(',');
            int n = int.Parse(size[0]);
            int m = int.Parse(size[1]);
            automaton = new List<autoState>();
            for (int i = 0; i < n; i++)
            {
                autoState state = new autoState();
                state.keyStates = new Dictionary<Pitch,int>();
                state.keyActions = new Dictionary<Pitch,autoState.action>();
                state.keyArgs = new Dictionary<Pitch,string>();
                automaton.Add(state);
            }
            for(int i = 0; i < m; i++)
            {
                // Formatted: source, pedal, target, action, arg
                string[] s = tr.ReadLine().Split(',');
                autoState state = automaton[int.Parse(s[0])];
                Console.WriteLine(s[1] + " " + s[3] + ".");
                if(s[1] == "1"){
                        state.p1State = int.Parse(s[2]);
                        state.p1Action = (autoState.action)Enum.Parse(typeof(autoState.action), s[3]);
                        Console.WriteLine(state.p1Action);
                        state.p1Arg = s[4];
                }
                else if (s[1] == "2"){ 
                        state.p2State = int.Parse(s[2]);
                        state.p2Action = (autoState.action)Enum.Parse(typeof(autoState.action), s[3]);
                        state.p2Arg = s[4];
                }
                else if (s[1] == "12")
                {
                    state.p12State = int.Parse(s[2]);
                    state.p12Action = (autoState.action)Enum.Parse(typeof(autoState.action), s[3]);
                    state.p12Arg = s[4];
                }
                else
                {
                    Pitch p = (Pitch)Enum.Parse(typeof(Pitch), s[1]);
                    state.keyStates[p] = int.Parse(s[2]);
                    state.keyActions[p] = (autoState.action)Enum.Parse(typeof(autoState.action), s[3]);
                    state.keyArgs[p] = s[4];
                }
            }
        }

        static void processTransition(int target, autoState.action action, string arg)
        {
            Console.WriteLine(target + " " + action + " " + arg);
            currentAutoState = target;
            switch(action){
                case autoState.action.none: break;
                case autoState.action.down: Process.Start("pagedown.exe"); break;
                case autoState.action.up: Process.Start("pageup.exe"); break;
                case autoState.action.play: playAudio(arg); break;
                case autoState.action.exit: Process.Start("closeMusic.exe"); break;
            }
        }
*/
        static void playAudio(string filename)
        {
            player.URL = filename;
        }

#if debug
        static void debugPrint(ControlChangeMessage msg, int value)
        {
            Console.WriteLine(msg.Control.ToString() + " " + msg.Control.Name() + " " + 
                msg.Channel.Name() + " " + msg.Channel + " " + msg.Value + " " + 
                msg.Device.Name + " " + msg.Device.ToString() + " " + value);
        }
#endif
    }
}
