using Debug = UnityEngine.Debug;
using KModkit;
using Rnd = UnityEngine.Random;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public class OmegaDestroyerScript : MonoBehaviour {

#region Variables

	//Changable constants
	private const string VERSION = "1.1.0";
	private const float TIME_DELAY = 2f;
	private const int INITIAL_TIME = 210;
	private const int RESETS_BEFORE_FULL = 8;
	private const int KYBER_RESET_TIME = 210;
	private const int GRACE_PERIOD = 10; //Grace for TP only
	private const int FULL_RESET_TIME = INITIAL_TIME +
		KYBER_RESET_TIME * RESETS_BEFORE_FULL + GRACE_PERIOD;

	//Unchangable constants
	private const byte NUMBERED_BUTTONS = 10;
	private const byte NON_MAIN_NUMBERS = 2;
	private const string DIGITS = "0123456789";
	private long[] MWYTH_NUMBERS = {55363537, 55363573, 55365337,
		55365373, 55633537, 55633573, 55635337, 55635373};
	private byte[] PRIMES = {2, 3, 5, 7, 11, 13, 17,
		19, 23, 29, 31, 37}; //sqrt(max possible k) < 41

	//KTANE-given constants
	public KMAudio Audio;
	public KMBombModule Module;
	public KMBossModule Boss;
	public KMBombInfo Bomb;
	public TextMesh[] Numbers;
	public Renderer[] ColorChanger;
	public KMSelectable[] Buttons;
	public Material[] Lights;
	public Material[] BColors;
	public Renderer[] BCChanger;
	public Light[] Lightarray;
	static private int _moduleIdCounter = 1;
	private int _moduleId;

	//Non-constants
	private int cycles; //IMPORTANT
	private Stopwatch tlHoldTime;
	private sbyte nextButton; //987654321043210 repeating
	private enum BRActionSet {CLEAR, FULL_RESET, HARD_MODE_START};
	private BRActionSet BRAction;
	private List<int>[] rotors;
	private bool locked, solved, split, muted, justReset, showingPAINs,
		deactivate, mwyth, skipNextCheck, shouldStop, BRBigAction, justStruck;
	private long alpha, beta, omega, k, grace_k, t, Rv;
	private bool[] swaps; //TL, TR, BL, BR
	private long[] PAINs; //P1-P4
	private string input;

#endregion
#region Startup

	void Awake() {
		_moduleId = _moduleIdCounter++;

		for(byte i = 0; i < Buttons.Length; i++) {
			KMSelectable btn = Buttons[i];
			btn.OnInteract += delegate {
				HandlePress(btn);
				return false;
			};
		} //Special delegation for TL release
		Buttons[10].OnInteractEnded += delegate {
			if(tlHoldTime.IsRunning && !mwyth) {
				tlHoldTime.Stop();
				StartCoroutine(TLUp());
			}
		};
	}
	
	void Start() {
		locked = true; //Lock all buttons while lights off
		//Log start message, then set most flags and initial values
		Debug.LogFormat("[OmegaDestroyer #{0}]: Version {1} of OmegaDestroyer started. Good luck!", _moduleId, VERSION);
		solved = muted = deactivate = mwyth = skipNextCheck
			= shouldStop = BRBigAction = justStruck = false;
		BRAction = BRActionSet.CLEAR; //Initially, BR just clears input
		tlHoldTime = new Stopwatch();
		swaps = new bool[4];
		PAINs = new long[4];
		nextButton = 9; //Input and rotors set in FullReset()
		for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
			BCChanger[i].material = BColors[5];
		StartCoroutine(TrueStart());
	}

	IEnumerator TrueStart() {
		if(!Application.isEditor) { //Skip intro in editor
			for(byte i = 0; i < 2; i++) {
				Audio.PlaySoundAtTransform("PressBeep", Numbers[0].transform);
				yield return new WaitForSeconds(1f);
			} //2.0s
			for(byte i = 0; i < 4; i++) {
				Audio.PlaySoundAtTransform("PressBeep", Numbers[0].transform);
				yield return new WaitForSeconds(0.5f);
			} //4.0s
			for(byte i = 0; i < 4; i++) {
				Audio.PlaySoundAtTransform("PressBeep", Numbers[0].transform);
				yield return new WaitForSeconds(0.25f);
			} //5.0s
			for(byte i = 0; i < 6; i++) {
				Audio.PlaySoundAtTransform("PressBeep", Numbers[0].transform);
				yield return new WaitForSeconds(0.1f);
			} //5.6s
			for(byte i = 0; i < 20; i++) {
				Audio.PlaySoundAtTransform("PressBeep", Numbers[0].transform);
				yield return new WaitForSeconds(0.02f);
			} //6.0s
		} //Once the intro is done, start the module
		FullReset(false); //SafetyCheck only started here
		StartCoroutine(SafetyCheck());
	}

#endregion
#region Resets

	void FullReset() {
		FullReset(true);
	}

	void FullReset(bool stop) {
		locked = true;
		if(stop)
			StopCycle();
		Debug.LogFormat("[OmegaDestroyer #{0}]: ====FULL RESET STARTED====", _moduleId);
		t = INITIAL_TIME; //Reset most values to initial
		ResetRotors(); //Even rotors, to be safe
		split = showingPAINs = false;
		k = 0; //For grace_k
		input = "";

		Numbers[1].color = Color.black; //Reset text colors too
		for(byte i = NON_MAIN_NUMBERS; i < Numbers.Length; i++)
			Numbers[i].color = mwyth ? Color.yellow : Color.black;

		//Randomize quadratic values
		alpha = Rnd.Range(1, 10000);
		string bString; //Temporary string to store beta in
		do { //No 0s or 9s allowed in beta
			beta = Rnd.Range(11111, 8888889);
			bString = beta.ToString();
		} while(bString.Contains("0") || bString.Contains("9"));
		omega = Rnd.Range(10000000, 100000000);

		Debug.LogFormat("[OmegaDestroyer #{0}]: The module has fully reset with the following values: α={1}, β={2}, ω={3}", _moduleId, alpha, beta, omega);
		StartCycle(); //Log values, resume cycling, and unlock buttons after a full reset
		skipNextCheck = true; //Ignore the check after a full reset
		StartCoroutine(DelayedUnlock());
	}

	void KYBERReset() {
		grace_k = k; //Store previous k for TP grace period
		int k_large = Rnd.Range(0, 100);
		int k_small = Rnd.Range(1, 15);
		k = 16 * k_large + k_small;
		if(justReset) //Log KYBER immediately after a full reset (for convenience in deciphering)
			Debug.LogFormat("[OmegaDestroyer #{0}]: The initial KYBER is {1}, which swaps according to {2}.", _moduleId, k, k_small);
		if(!muted && !Application.isEditor) //If the module isn't muted, play a sound to indicate the KYBER reset
			Audio.PlaySoundAtTransform("KYBERChanged", Numbers[0].transform);

		//Display k_large, and set swap flags to match bits of k_small
		Numbers[0].text = k_large.ToString("00");
		for(byte i = 0; i < swaps.Length; i++)
			swaps[i] = (k_small & (0x8 >> i)) > 0;

		if(mwyth) { //Hard mode extra scrambling
			for(byte i = 0; i < PAINs.Length; i++)
				PAINs[i] = Rnd.Range(10, 100);
			while(PAINs[0] == 55) //Prevent the PAINs from showing a MWYTH number
				PAINs[0] = Rnd.Range(10, 100);
			if(showingPAINs) //Update to show new PAINs
				UpdateDisplays();
		}
	}

	void ResetRotors() {
		rotors = new List<int>[] { //Listed in position 0- they might move
			(new int[] {6, 2, 0, 9, 1, 7, 5, 8, 4, 3}).ToList(), //0
			(new int[] {7, 8, 1, 0, 6, 4, 2, 9, 3, 5}).ToList(), //1
			(new int[] {9, 6, 4, 2, 5, 3, 0, 8, 1, 7}).ToList(), //2
			(new int[] {5, 0, 3, 7, 1, 8, 9, 4, 6, 2}).ToList(), //3
			(new int[] {3, 9, 7, 5, 2, 1, 4, 6, 0, 8}).ToList(), //4
			(new int[] {8, 7, 6, 1, 0, 9, 3, 5, 2, 4}).ToList(), //5
			(new int[] {1, 3, 5, 8, 7, 4, 0, 2, 9, 6}).ToList(), //6
			(new int[] {2, 4, 9, 6, 8, 0, 1, 3, 7, 5}).ToList(), //7
			(new int[] {4, 9, 8, 2, 3, 6, 7, 1, 5, 0}).ToList(), //8
			(new int[] {7, 5, 6, 4, 9, 2, 8, 0, 3, 1}).ToList()  //9
		};
	}

#endregion
#region Safety Stuff

	void StartCycle() {
		if(cycles != 0) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Likely spam-caused: Module attempted to start cycling, but it was already cycling.", _moduleId);
			return;
		}
		cycles++;
		StartCoroutine(MainCycle());
	}

	void StopCycle() {
		if(cycles == 0) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Likely spam-caused: Module attempted to stop cycling, but it was already stopped.", _moduleId);
			return;
		}
		cycles--;
		shouldStop = true;
	}

	IEnumerator DelayedUnlock() { //Unlock with anti-spam precautions
		yield return new WaitForSeconds(TIME_DELAY);
		locked = false;
	}

	IEnumerator SafetyCheck() { //Checks if MainCycle is running exactly once when it should be
		while(true) {
			long t_check = t;
			int checks = 0; //3 t-steps must occur to conclude full check
							//If the module locked input, it might have stopped the timer, so ignore this check
			yield return new WaitForSeconds(0.1f); //Also wait between checks to avoid an infinite loop of checking
			while(t_check == t && !locked && checks < 20 * TIME_DELAY) {
				yield return new WaitForSeconds(0.1f);
				checks++;
			}

			if(solved) //Stop method only when module solved, such that there's no way it stops early
				yield break;
			if(locked || t < t_check) //Ignore check if inputs were locked or time went down (i.e. full reset)
				continue;
			if(skipNextCheck) { //Ignore the first check after each strike to avoid excess trigger if strike bugfix activates
				skipNextCheck = false;
				continue;
			}

			if(checks <= 6 * TIME_DELAY || t > t_check + 1) {
				locked = true; //If the module went too fast, stop and restart it
				Debug.LogFormat("[OmegaDestroyer #{0}]: LIKELY BUG: SAFETY CHECK FOR EXCESS CYCLES TRIPPED", _moduleId);
				for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
					BCChanger[i].material = BColors[2];
				for(int i = 0; i < cycles + 1; i++) {
					shouldStop = true; //Manually stop all known cycles, plus 1 more
					yield return new WaitForSeconds(1.5f * TIME_DELAY);
				} //If there are 2+ unknown cycles, 1 will be stopped at a time (stop 2, start 1)
				Numbers[2].text = "SA";
				Numbers[3].text = "FE";
				Numbers[4].text = "TY";
				Numbers[5].text = "--";
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				yield return new WaitForSeconds(0.5f);
				Numbers[2].text = "ST";
				Numbers[3].text = "OP";
				Numbers[4].text = "PE";
				Numbers[5].text = "R-";
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				yield return new WaitForSeconds(0.5f);
				Numbers[2].text = "FI";
				Numbers[3].text = "NI";
				Numbers[4].text = "SH";
				Numbers[5].text = "ED";
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				yield return new WaitForSeconds(0.5f);
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
					BCChanger[i].material = BColors[mwyth ? 0 : 5];
				cycles = 0; //Update cycle counter: it should be 0 at this point
				t++; //Jump to next t immediately
				locked = shouldStop = false;
				StartCycle();
			} else if(checks >= 20 * TIME_DELAY) {
				locked = true; //If the module stopped, immediately restart it
				Debug.LogFormat("[OmegaDestroyer #{0}]: LIKELY BUG: SAFETY CHECK FOR STOPPED CYCLE TRIPPED", _moduleId);
				for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
					BCChanger[i].material = BColors[2];
				Numbers[2].text = "SA";
				Numbers[3].text = "FE";
				Numbers[4].text = "TY";
				Numbers[5].text = "--";
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				yield return new WaitForSeconds(0.5f);
				Numbers[2].text = "RE";
				Numbers[3].text = "-S";
				Numbers[4].text = "TA";
				Numbers[5].text = "RT";
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				yield return new WaitForSeconds(0.5f);
				if(!Application.isEditor)
					Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
				for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
					BCChanger[i].material = BColors[mwyth ? 0 : 5];
				cycles = 0; //Update cycle counter: it should be 0 at this point
				t++; //Jump to next t immediately
				locked = shouldStop = false;
				StartCycle();
			}
		}
	}

#endregion
#region Cycles/Updates

	IEnumerator MainCycle() {
		justReset = true;
		while(true) {
			if(!justReset) { //No wait/increment after reset
				yield return new WaitForSeconds(TIME_DELAY / 2); //Comment out to test safety STOPPER
				yield return new WaitForSeconds(TIME_DELAY / 2);
				if(shouldStop) { //Stop coroutine here if it should stop
					shouldStop = false;
					yield break;
				}

				//continue; //Uncomment to test safety RESTART (must stay commented to test safety STOPPER)
				t++;
			}

			if(t >= FULL_RESET_TIME) {
				FullReset(false);
				yield break;
			} //If it's time for a full reset, do so
			ColorCountdown(); //Else, handle KYBER countdown and Rv update
			Rv = (alpha * t * t + beta * t + omega) % 100000000;
			if(MWYTH_NUMBERS.Contains(Rv)) { //Automatic hard mode start (logs when it happened)
				Debug.LogFormat("[OmegaDestroyer #{0}]: Rv has become {1} at time {2}.", _moduleId, Rv, t);
				if(mwyth) {
					Debug.LogFormat("[OmegaDestroyer #{0}]: MWYTH is already active, so this value will be dashed out.", _moduleId);
					Rv = 4545454545;
				} else {
					StartCoroutine(MWYTHOn(false));
					yield break;
				}
			}

			//THIS CODE INTENTIONALLY NOT DONE BY UPDATE DISPLAYS METHOD TO AVOID CONFLICTS
			if(!split) { //Only update any displays if not split
				Numbers[1].text = (t < 1000 ? "!" : "") + t.ToString();
				//Only show Rv if not showing input or PAINs
				if(input.Length == 0 && !showingPAINs) {
					string RvString = MWYTH_NUMBERS.Contains(Rv)
						? "--------" : Rv.ToString("00000000");
					for(byte i = 0; i < swaps.Length; i++) {
						//1. Get the (i+1)th digit pair
						string digitPair = RvString.Substring(2*i, 2);
						//2. Reverse it if the (n+1)th swap flag is true
						string displayPair = swaps[i] ? //(String reversal is weird)
							new string(digitPair.Reverse().ToArray()) : digitPair;
						//3. Set the (i+1)th main display's text
						Numbers[NON_MAIN_NUMBERS + i].text = displayPair;
					}
				}
			} //Must loop once to not have just struck
			justStruck = justReset;
			justReset = false;
		}
	}

	void ColorCountdown() { //Time 0 on reset, loops to 0 every 210 t-steps
		byte time = (byte)((t - (INITIAL_TIME % 210)) % 210);
		if(time == 0) { //Time expired: Reset KYBER and colors
			KYBERReset(); //This is where the KYBER is reset in a full reset
			for(byte i = 0; i < NUMBERED_BUTTONS; i++)
				BCChanger[i].material = BColors[3];
			nextButton = 9;
		} else if(time < 18 * 11) { //Change 1 to yellow every 36s (18 t-steps)
			if(time % 18 == 0) {
				BCChanger[nextButton].material = BColors[2];
				nextButton--;
				if(nextButton < 0)
					nextButton = 4;
			}
		} else { //Final 24 seconds: Change 2 to red every 6s (3 t-steps)
			if(time % 3 == 0) {
				BCChanger[nextButton + 5].material = BColors[1];
				BCChanger[nextButton].material = BColors[1];
				nextButton--;
			}
		}
	}

	//This function is only for immediate updates.
	//It is separate from the main cycle timed updates.
	void UpdateDisplays() {
		if(!split) //Update timer if unsplit
			Numbers[1].text = (t < 1000 ? "!" : "") + t.ToString();
		if(input.Length > 0) { //Showing input (updates even when split)
			string inputString = MWYTH_NUMBERS.Contains(long.Parse(input))
				? "--------" : input + "-------"; //Dashes applied to end
			for(byte i = 0; i < Numbers.Length - NON_MAIN_NUMBERS; i++) {
				//1. Get the (i+1)th digit pair as a string
				string digitPair = inputString.Substring(2 * i, 2);
				//2. Set the (i+1)th main display's text and color
				Numbers[NON_MAIN_NUMBERS + i].text = digitPair;
				Numbers[NON_MAIN_NUMBERS + i].color = Color.magenta;
			}
		} else if(!split) { //Otherwise, only update displays if not split
			if(showingPAINs) { //Showing PAINs (new code)
				for(byte i = 0; i < Numbers.Length - NON_MAIN_NUMBERS; i++) {
					//1. Get the (i+1)th PAIN as a string
					string PAINString = PAINs[i].ToString("00");
					//2. Set the (i+1)th main display's text and color
					Numbers[NON_MAIN_NUMBERS + i].text = PAINString;
					Numbers[NON_MAIN_NUMBERS + i].color = Color.cyan;
				}
			} else { //Showing Rv (mostly same code as main cycle)
				string RvString = MWYTH_NUMBERS.Contains(Rv)
					? "--------" : Rv.ToString("00000000");
				for(byte i = 0; i < Numbers.Length - NON_MAIN_NUMBERS; i++) {
					//1. Get the (i+1)th digit pair as a string
					string digitPair = RvString.Substring(2 * i, 2);
					//2. Reverse it if the (n+1)th swap flag is true
					string displayPair = swaps[i] ? //(String reversal is weird)
						new string(digitPair.Reverse().ToArray()) : digitPair;
					//3. Set the (i+1)th main display's text and color
					Numbers[NON_MAIN_NUMBERS + i].text = displayPair;
					Numbers[NON_MAIN_NUMBERS + i].color = mwyth
						? Color.yellow : Color.black;
				}
			}
		}
	}

#endregion
#region Press Actions

	void HandlePress(KMSelectable btn) {
		int pressed = Array.IndexOf(Buttons, btn);
		Buttons[pressed].AddInteractionPunch();
		if((!muted || solved) && !Application.isEditor)
			Audio.PlaySoundAtTransform("PressBeep", Numbers[0].transform);
		if(deactivate && pressed == 10 && !solved)
			MWYTHOff();
		if(solved || locked)
			return;

		switch(pressed) {
			case 10: { //TL
					   //Stop stopwatch if it's running
				if(tlHoldTime.IsRunning)
					tlHoldTime.Stop();
				if(mwyth) { //Hard mode: toggle PAINs
					showingPAINs = !showingPAINs;
					UpdateDisplays();
				} else { //Otherwise: start stopwatch
					tlHoldTime.Reset();
					tlHoldTime.Start();
				}
				break;
			}
			case 11: { //TR
				if(!justStruck)
					Submit();
				break;
			}
			case 12: { //BL
				split = !split;
				if(split) //Timer blue if split
					Numbers[1].color = Color.blue;
				else { //Timer black if unsplit
					Numbers[1].color = Color.black;
					UpdateDisplays();
				} //Also update displays on unsplitting
				break;
			}
			case 13: { //BR
				BRDown();
				break;
			}
			default: {
				Input(pressed);
				break;
			}
		}
	}

	IEnumerator TLUp() {
		long elapsed = tlHoldTime.ElapsedMilliseconds;
		tlHoldTime.Reset();
		if(elapsed < 3000) { //<3 seconds
			muted = !muted; //Toggle mute and update TL color
			BCChanger[10].material = BColors[muted ? 4 : 5];
		} else {
			if(elapsed < 10000) { //3-10 seconds
				BRAction = BRActionSet.FULL_RESET;
				BCChanger[13].material = BColors[2];
			} else { //10+ seconds (Manual hard mode start setup)
				BRAction = BRActionSet.HARD_MODE_START;
				BCChanger[13].material = BColors[1];
			}

			yield return new WaitForSeconds(1f);
			BRAction = BRActionSet.CLEAR; //Reset action after 1s
			if(!BRBigAction && !locked) //Only reset color if appropriate
				BCChanger[13].material = BColors[mwyth ? 0 : 5];
			else //If it is doing something big, reset the flag
				BRBigAction = false;
		}
	}

	void BRDown() {
		BRBigAction = BRAction != BRActionSet.CLEAR;
		switch(BRAction) {
			case BRActionSet.HARD_MODE_START: {
				StartCoroutine(MWYTHOn());
				break;
			}
			case BRActionSet.FULL_RESET: {
				FullReset();
				break;
			}
			default: { //BRActionSet.CLEAR
				Clear();
				break;
			}
		} //Reset BR color/action IMMEDIATELY
		if(BRAction != BRActionSet.HARD_MODE_START)
			BCChanger[13].material = BColors[mwyth ? 0 : 5];
		BRAction = BRActionSet.CLEAR; //(Anti-spam check)
	}

	void Input(int pressed) {
		if(input.Length >= 8) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Maximum input length exceeded. Striking...", _moduleId);
			StartCoroutine(Strike()); //Input too big: strike and return
			return;
		} //Append and update
		input += pressed;
		UpdateDisplays();
	}

	//Do not use when fully resetting
	void Clear() {
		input = "";
		UpdateDisplays();
	}

#endregion
#region Submission

	void Submit() {
		locked = true; //Lock input while submitting
		Debug.LogFormat("[OmegaDestroyer #{0}]: ====SUBMISSION ATTEMPT====", _moduleId);
		Debug.LogFormat("[OmegaDestroyer #{0}]: Submitted '{1}' at t={2}, with k={3}.", _moduleId, input, t, k);
		if(input.Length != 8) { //Input must be exactly 8 long
			Debug.LogFormat("[OmegaDestroyer #{0}]: Insufficient input length. Striking...", _moduleId);
			StartCoroutine(Strike());
			return;
		}

		long unfactored = k; //Time check
		List<long> factors = new List<long>(10);
		for(byte i = 0; i < PRIMES.Length; i++) {
			//While a factor divides, append and divide it
			while(unfactored % PRIMES[i] == 0) {
				factors.Add(PRIMES[i]);
				unfactored /= PRIMES[i];
			} //Once a factor stops dividing, move to the next factor
		} //Once unfactored part is prime or 1, add it to list
		if(unfactored != 1)
			factors.Add(unfactored);
		factors.Add(1); //Only add a single 1
		factors.Sort(); //Logging stuff
		long factorSum = factors.Sum();
		Debug.LogFormat("[OmegaDestroyer #{0}]: k={1} creates the factor list {2}, which adds to {3}.", _moduleId, k, ArrayToString(factors.ToArray()), factorSum);
		Debug.LogFormat("[OmegaDestroyer #{0}]: You submitted when the last digit of t was {1}, when it should be {2}.", _moduleId, t % 10, factorSum % 10);

		if(t % 10 != factorSum % 10) { //Actual check happens here
			Debug.LogFormat("[OmegaDestroyer #{0}]: Invalid submission time. Striking...", _moduleId);
			StartCoroutine(Strike());
			return;
		} //If both checks passed, start the actual calculations
		Debug.LogFormat("[OmegaDestroyer #{0}]: Length and submission time are valid, proceeding to calculations...", _moduleId);
		Debug.LogFormat("[OmegaDestroyer #{0}]: Rv = ({2}*{1}^2 + {3}*{1} + {4}) mod 100,000,000 = {5}", _moduleId, t, alpha, beta, omega, Rv);

		//Separate Rv into digits for ciphering
		char[] RvChars = Rv.ToString("00000000").ToCharArray();
		long[] RvDigits = new long[RvChars.Length];
		//This COMPLETELY fills the array with digits
		for(byte i = 0; i < RvDigits.Length; i++)
			RvDigits[i] = RvChars[i] - '0';

		AlphaCipher(RvDigits, alpha); //Standard Alpha Cipher
		if(mwyth) { //Hard mode extra step 1: repeat Alpha Cipher with PAINs as key starts
			Debug.LogFormat("[OmegaDestroyer #{0}]: MWYTH is active with PAINs {1}. Repeating Alpha Cipher...", _moduleId, ArrayToString(PAINs));
			for(byte i = 0; i < PAINs.Length; i++)
				AlphaCipher(RvDigits, PAINs[i]);
		} //End of Alpha Cipher

		//Fill the start of the string for Beta Cipher with beta's distinct characters
		string betaString = new string(beta.ToString().Distinct().ToArray());
		for(char i = '1'; i < '9'; i++) { //Add most unused characters
			if(!betaString.Contains(i))
				betaString += i;
		} //Add '0' if k is under 1000, otherwise add '9'
		betaString += k < 1000 ? '0' : '9'; //Log the grid in string form
		Debug.LogFormat("[OmegaDestroyer #{0}]: k={1}, so the Beta Cipher grid in reading order is {2}.", _moduleId, k, betaString);
		char[] betaChars = betaString.ToCharArray(); //Turn string to int[]
		int[] betaGrid = new int[betaChars.Length];
		for(byte i = 0; i < betaChars.Length; i++)
			betaGrid[i] = betaChars[i] - '0';

		int[,] coordinates = new int[4,4]; //2D grid of coordinates
		for(byte i = 0; i < RvDigits.Length; i++) {
			//Find the Rv digit and put its location in the coordinate grid
			int row = i / 2; //00, 02, 10...
			int leftCol = (i * 2) % 4;
			int index = Array.IndexOf(betaGrid, (int)RvDigits[i]);
			int coordRow = index == -1 ? 0 : index / 3;
			int coordCol = index == -1 ? 0 : index % 3;
			coordinates[row, leftCol] = coordCol;
			coordinates[row, leftCol + 1] = coordRow;
		} //Log the coordinate string using an overload of ArrayToString
		Debug.LogFormat("[OmegaDestroyer #{0}]: Coordinate string, row by row: {1}", _moduleId, ArrayToString(coordinates));

		if(mwyth) { //Hard mode extra step 2: modify coordinates using PAINs' first digits
			for(byte row = 0; row < 4; row++) {
				for(byte col = 0; col < 4; col++) {
					byte offset = (byte)(PAINs[row < 2 ? (col < 2 ? 0 : 1)
						: (col < 2 ? 2 : 3)] / 10); //Add offset then mod 3
					coordinates[row, col] = (coordinates[row, col] + offset) % 3;
				} //End of column loop
			} //End of row loop
			Debug.LogFormat("[OmegaDestroyer #{0}]: MWYTH quadrant offsets in reading order: {1}, {2}, {3}, {4}",
				_moduleId, PAINs[0] / 10, PAINs[1] / 10, PAINs[2] / 10, PAINs[3] / 10); //Log offsets and new coordinates
			Debug.LogFormat("[OmegaDestroyer #{0}]: Modified string, row by row: {1}", _moduleId, ArrayToString(coordinates));
		} //End of if statement

		//Read in column reading order and lookup new coordinates
		for(byte i = 0; i < RvDigits.Length; i++) {
			int col = i / 2; //00, 02, 10...
			int topRow = (i * 2) % 4;
			int coordCol = coordinates[topRow, col];
			int coordRow = coordinates[topRow + 1, col];
			int index = coordRow * 3 + coordCol;
			RvDigits[i] = betaGrid[index];
		} //End of Beta Cipher
		Debug.LogFormat("[OmegaDestroyer #{0}]: Rv's digits are {1} after the coordinate lookup.", _moduleId, ArrayToString(RvDigits));

		OmegaCipher(RvDigits); //Omega Cipher is big, so it has its own giant method
		for(byte i = 0; i < RvChars.Length; i++) //Re-catenating Rv (now Cv)
			RvChars[i] = (char)(RvDigits[i] + '0');
		long Cv = long.Parse(new string(RvChars));
		Debug.LogFormat("[OmegaDestroyer #{0}]: After all three ciphers, Cv = {1}", _moduleId, Cv);

		long Fv = (Cv + alpha * beta + k * t * omega) % 100000000;
		string FvString = Fv.ToString("00000000"); //Final calculations
		Debug.LogFormat("[OmegaDestroyer #{0}]: Fv = ({1} + {2}*{3} + {4}*{5}*{6}) mod 100,000,000 = {7}",
			_moduleId, Cv, alpha, beta, k, t, omega, Fv); //Final logging and the ultimate check
		Debug.LogFormat("[OmegaDestroyer #{0}]: THE EXPECTED PASSWORD IS {1}. YOUR SUBMITTED PASSWORD WAS {2}.", _moduleId, FvString, input);
		if(input.Equals(FvString)) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Authentication confirmed. Module solved.", _moduleId);
			Solve();
		} else {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Incorrect password submitted. Striking...", _moduleId);
			StartCoroutine(Strike());
		}
	}

#endregion
#region Ciphers

	void AlphaCipher(long[] RvDigits, long keyStart) {
		string initialDigits = ArrayToString(RvDigits);
		//Separate the starting key into digits for ciphering
		char[] keyChars = keyStart.ToString().ToCharArray();
		long[] keyDigits = new long[RvDigits.Length];
		//This PARTIALLY fills the array with digits
		for(byte i = 0; i < keyChars.Length; i++)
			keyDigits[i] = keyChars[i] - '0';

		//Main autokey loop (works digit by digit)
		for(byte i = 0; i < RvDigits.Length; i++) {
			RvDigits[i] = (RvDigits[i] + keyDigits[i]) % 10;
			if(keyChars.Length + i < RvDigits.Length)
				keyDigits[keyChars.Length + i] = RvDigits[i];
		} //Extend key with calculated digit only if necessary

		Debug.LogFormat("[OmegaDestroyer #{0}]: Alpha Cipher with key {1} has changed the digits of Rv as follows:", _moduleId, keyStart);
		Debug.LogFormat("[OmegaDestroyer #{0}]: {1} + {2} => {3}", _moduleId, initialDigits, ArrayToString(keyDigits), ArrayToString(RvDigits));
	}

	void OmegaCipher(long[] RvDigits) {
		char[] omegaChars = omega.ToString().ToCharArray();
		int[] omegaDigits = new int[omegaChars.Length];
		for(byte i = 0; i < omegaDigits.Length; i++)
			omegaDigits[i] = omegaChars[i] - '0';

		long[] plugboard = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
		plugboard[omegaDigits[0]]--; //Plugboard swap
		plugboard[omegaDigits[0] - 1]++;
		int lowest = 0; //Lowest possible unused layout
						// First duplicate removal
		while(omegaDigits[2] == omegaDigits[1]) {
			omegaDigits[2] = lowest;
			lowest++;
		} //Second duplicate removal
		while(omegaDigits[3] == omegaDigits[2] || omegaDigits[3] == omegaDigits[1]) {
			omegaDigits[3] = lowest;
			lowest++;
		} //Third duplicate removal
		while(omegaDigits[4] == omegaDigits[3] || omegaDigits[4] == omegaDigits[2]
			|| omegaDigits[4] == omegaDigits[1]) {
			omegaDigits[4] = lowest;
			lowest++;
		} //All duplicates are now removed, making everything work

		//Reflector and rotor choices and starting positions (reflector then rotors)
		int[] positions = {0, omegaDigits[5], omegaDigits[6], omegaDigits[7]};
		List<int> reflector = rotors[omegaDigits[1]];
		List<int> bottom = rotors[omegaDigits[2]];
		List<int> middle = rotors[omegaDigits[3]];
		List<int> top = rotors[omegaDigits[4]];
		RotorCycle(bottom, positions[1]);
		RotorCycle(middle, positions[2]);
		RotorCycle(top, positions[3]);

		Debug.LogFormat("[OmegaDestroyer #{0}]: Omega is equal to {1}, which indicates the following:", _moduleId, omega);
		Debug.LogFormat("[OmegaDestroyer #{0}]: {1} and {2} are swapped on the plugboard, making the top row {3}.",
			_moduleId, omegaDigits[0], omegaDigits[0] - 1, ArrayToString(plugboard));
		Debug.LogFormat("[OmegaDestroyer #{0}]: The reflector is layout {1}, and the rotors are layouts {2}, {3}, and {4}.",
			_moduleId, omegaDigits[1], omegaDigits[2], omegaDigits[3], omegaDigits[4]);
		Debug.LogFormat("[OmegaDestroyer #{0}]: The initial positions of the rotors are {1}, {2}, and {3}.",
			_moduleId, positions[1], positions[2], positions[3]);
		if(mwyth) { //Hard mode extra step 3: additional rotor/reflector shifting
			Debug.LogFormat("[OmegaDestroyer #{0}]: MWYTH has further shifted things. The reflector was shifted by {1}.", _moduleId, PAINs[0] % 10);
			Debug.LogFormat("[OmegaDestroyer #{0}]: The rotors (bottom to top) were shifted by {1}, {2}, and {3}.", _moduleId, PAINs[1] % 10, PAINs[2] % 10, PAINs[3] % 10);
			RotorCycle(reflector, PAINs[0] % 10);
			RotorCycle(bottom, PAINs[1] % 10);
			RotorCycle(middle, PAINs[2] % 10);
			RotorCycle(top, PAINs[3] % 10);
			for(int i = 0; i < positions.Length; i++) {
				positions[i] = (positions[i] + (int)PAINs[i]) % 10;
			} //Updating positions to match additional shifts
		} //Now that the enigma is set up, use it

		for(int i = 0; i < RvDigits.Length; i++) {
			List<long> passed = new List<long>(9); //Tracker for log
			int current = (int)RvDigits[i];
			passed.Add(current); //Initial digit
			int index = Array.IndexOf(plugboard, current);
			current = top[index]; //Down to top
			passed.Add(current); //Within top
			index = (current - positions[3] + 10) % 10;
			current = middle[index]; //Down to middle
			passed.Add(current); //Within middle
			index = (current - positions[2] + 10) % 10;
			current = bottom[index]; //Down to bottom
			passed.Add(current); //Within bottom
			index = (current - positions[1] + 10) % 10;

			//From this point on, everything is upside down
			current = (index + positions[0]) % 10; //Down to reflector
			passed.Add(current); //Within reflector
			index = reflector.IndexOf(current);
			current = (index + positions[1]) % 10; //Up to bottom
			passed.Add(current); //Within bottom
			index = bottom.IndexOf(current);
			current = (index + positions[2]) % 10; //Up to middle
			passed.Add(current); //Within middle
			index = middle.IndexOf(current);
			current = (index + positions[3]) % 10; //Up to top
			passed.Add(current); //Within top
			index = top.IndexOf(current);

			current = (int)plugboard[index];
			passed.Add(current); //Up to plugboard
			RvDigits[i] = current; //Final digit and logging of track
			Debug.LogFormat("[OmegaDestroyer #{0}]: Digit #{1} path including start/end: {2}", _moduleId, i + 1, ArrayToString(passed.ToArray()));
			SmartRotorCycle(bottom, middle, top, positions, omegaDigits); //Rotor turn
		}
	}

	void SmartRotorCycle(List<int> bottom, List<int> middle,
		List<int> top, int[] positions, int[] omegaDigits) {
		//Note: each rotor's change position is identical to its layout number
		if(positions[2] == omegaDigits[3]) { //Triple rotor rotation
			RotorCycle(top);
			positions[3] = (positions[3] + 1) % 10;
			RotorCycle(middle);
			positions[2] = (positions[2] + 1) % 10;
			RotorCycle(bottom);
			positions[1] = (positions[1] + 1) % 10;
		} else if(positions[3] == omegaDigits[4]) { //Double rotor rotation
			RotorCycle(top);
			positions[3] = (positions[3] + 1) % 10;
			RotorCycle(middle);
			positions[2] = (positions[2] + 1) % 10;
		} else { //Single rotor rotation
			RotorCycle(top);
			positions[3] = (positions[3] + 1) % 10;
		}
	}

	void RotorCycle(List<int> rotor) {
		RotorCycle(rotor, 1);
	}

	void RotorCycle(List<int> rotor, long amount) {
		for(byte i = 0; i < amount; i++) {
			int cycling = rotor[0];
			rotor.RemoveAt(0);
			rotor.Add(cycling);
		}
	}

#endregion
#region Solve/Strike

	void Solve() {
		StopCycle();
		solved = true;
		muted = false;
		input = ""; //For special string possibility
		if(!deactivate) {
			if(!Application.isEditor)
				Audio.PlaySoundAtTransform("ModuleSolved", Numbers[0].transform);
			for(byte i = 0; i < Buttons.Length; i++)
				BCChanger[i].material = BColors[mwyth && i < NUMBERED_BUTTONS ? 4 : 3];
		} //Always change light colors
		ColorChanger[0].material = Lights[2];
		ColorChanger[1].material = Lights[2];
		Lightarray[0].color = Color.green;
		Lightarray[1].color = Color.green;

		Debug.LogFormat("[OmegaDestroyer #{0}]: ====MODULE SOLVED, GG!====", _moduleId);
		Module.HandlePass();
	}

	IEnumerator Strike() {
		locked = true;
		StopCycle(); //Pause screen while striking
		Debug.LogFormat("[OmegaDestroyer #{0}]: ====INCORRECT PASSWORD====", _moduleId);
		Module.HandleStrike();

		if(!Application.isEditor)
			Audio.PlaySoundAtTransform("ModuleStruck", Numbers[0].transform);
		ResetRotors(); //Reset rotors and make lights red
		ColorChanger[0].material = Lights[1];
		ColorChanger[1].material = Lights[1];
		Lightarray[0].color = Color.red;
		Lightarray[1].color = Color.red;
		for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
			BCChanger[i].material = BColors[1];
		yield return new WaitForSeconds(1f); //Delay for showing strike effect
		ColorChanger[0].material = Lights[0];
		ColorChanger[1].material = Lights[0];
		Lightarray[0].color = Color.black;
		Lightarray[1].color = Color.black;
		justStruck = true; //Anti-spam on submit button

		if(mwyth) { //Hard mode strike (full reset)
			deactivate = true;
			StartCoroutine(MWYTHOn(false));
		} else { //Normal strike (no full reset)
			Clear(); //Reset display colors and restart cycle
			for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
				BCChanger[i].material = BColors[i == 10 && muted ? 4 : 5];
			StartCycle();
			locked = false;
		}
	}

#endregion
#region Hard Mode

	IEnumerator Alert() {
		if(Application.isEditor)
			yield break; //Don't alert in editor
		for(byte i = 0; i < 4; i++) {
			Audio.PlaySoundAtTransform("AlertTone", Numbers[0].transform);
			yield return new WaitForSeconds(2.25f);
		} //Play alert tone 5 times total, only wait after first 4
		Audio.PlaySoundAtTransform("AlertTone", Numbers[0].transform);
	}

	IEnumerator MWYTHOn() {
		StartCoroutine(MWYTHOn(true));
		yield break;
	}

	IEnumerator MWYTHOn(bool stop) {
		locked = mwyth = true;
		if(stop)
			StopCycle();
		Debug.LogFormat("[OmegaDestroyer #{0}]: Oh no. (MWYTH activating...)", _moduleId);

		for(byte i = 0; i < Buttons.Length; i++)
			BCChanger[i].material = BColors[1];
		Numbers[1].text = "OHNO";
		for(byte i = NON_MAIN_NUMBERS; i < Numbers.Length; i++)
			Numbers[i].text = "";
		if(deactivate) { //Deactivation indicator
			Numbers[2].color = Color.green;
			Numbers[2].text = "EZ";
		} else { //Initial activation alert and log message
			Debug.LogFormat("[OmegaDestroyer #{0}]: Deactivation will unlock after a strike on this module.", _moduleId);
			StartCoroutine(Alert());
		} //Main countdown
		for(float i = 9.9f; i > -0.05f; i -= 0.1f) {
			Numbers[0].text = i.ToString("0.0");
			yield return new WaitForSeconds(0.1f);
		} //End of countdown
		
		deactivate = false;
		if(mwyth) { //If still active, buttons become black
			Debug.LogFormat("[OmegaDestroyer #{0}]: MWYTH activation complete.", _moduleId);
			for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
				BCChanger[i].material = BColors[0];
			muted = true; //Also, the module force-mutes
		} //No matter what, it fully resets
		FullReset(false);
	}

	void MWYTHOff() {
		muted = deactivate = mwyth = false; //Reset MWYTH effects and deactivation flag
		Debug.LogFormat("[OmegaDestroyer #{0}]: MWYTH deactivated. The module will still fully reset.", _moduleId);
		for(byte i = NUMBERED_BUTTONS; i < Buttons.Length; i++)
			BCChanger[i].material = BColors[5];
		Numbers[2].text = "";
	}

#endregion
#region ArrayToString

	string ArrayToString(long[] arr) { //Way of printing array contents (changable)
		string str = "";
		bool comma = false;
		for(byte i = 0; i < arr.Length; i++) {
			if(comma)
				str += ", ";
			str += arr[i].ToString();
			comma = true;
		}
		return "[" + str + "]";
	}

	string ArrayToString(int[,] arr) { //Way of printing 4*4 array contents (changable)
		string str = "";
		bool externalComma = false;
		for(byte row = 0; row < 4; row++) {
			if(externalComma)
				str += ", ";
			bool internalComma = false;

			str += "[";
			for(byte col = 0; col < 4; col++) {
				if(internalComma)
					str += ", ";
				str += arr[row, col].ToString();
				internalComma = true;
			}
			str += "]";

			externalComma = true;
		}
		return str;
	}

#endregion
#region Twitch Plays

	#pragma warning disable 414
	string TwitchHelpMessage = "Use !{0} time (Display current module time) || [press] <digits> (Input a password) || [press] <display> [at <time>] (Press a display, use !{0} displays for a list of usable display names) || hold tl for <duration> then press br (Duration must be a whole number between 3 and 11 inclusive).";
	#pragma warning restore 414

	IEnumerator ProcessTwitchCommand(string command) {
		Debug.LogFormat("[OmegaDestroyer #{0}]: Twitch Plays command received: '{1}'", _moduleId, command);
		if(tlHoldTime.IsRunning) {
			tlHoldTime.Stop();
			tlHoldTime.Reset();
		} //Stop and reset TL hold time in case a command was cancelled
		bool waited = false;
		command = command.ToLowerInvariant().Trim();
		if(locked) { //Don't allow commands when locking...
			if(deactivate && Regex.IsMatch(command, "^(tl|mute)$")) { //...except for deactivation...
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: deactivate MWYTH (TL)", _moduleId);
				Buttons[10].OnInteract();
				yield return new WaitForSeconds(0.1f);
				yield return null;
				Buttons[10].OnInteractEnded();
				yield break;
			} else if(command.Equals("displays")) { //...and showing the list of display names
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: show display list", _moduleId);
				yield return "sendtochat Names for TL: tl / mute || Names for TR: tr / submit / destroy / destructinate || Names for BL: bl / split || Names for BR: br / clear";
				yield break;
			} else { //If it's neither of those, it can't be done at this time
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: inputs locked (INVALID)", _moduleId);
				yield return "sendtochaterror The module has locked inputs for a time.";
				yield break;
			}
		}
		
		if(command.Equals("displays")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: show display list", _moduleId);
			yield return "sendtochat Names for TL: tl / mute || Names for TR: tr / submit / destroy / destructinate || Names for BL: bl / split || Names for BR: br / clear";
			yield break;
		} else if(command.Equals("time")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: show current time", _moduleId);
			yield return "sendtochat Current elapsed time: " + t;
			yield break;
		} else if(command.StartsWith("hold tl for ") && command.EndsWith(" then press br")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: hold TL, press BR", _moduleId);
			if(mwyth) { //TL holding is blocked in hard mode
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: MWYTH active (INVALID)", _moduleId);
				yield return "sendtochaterror Cannot hold TL while MWYTH is active.";
				yield break;
			} //Remove everything but the duration from the command
			command = command.Replace("hold tl for ", "").Replace(" then press br", "").Trim();
			int duration; //Attempt to parse the duration
			bool parsed = int.TryParse(command, out duration);
			if(!parsed || duration < 3 || duration > 11) {
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: given duration '{1}' bad (INVALID)", _moduleId, command);
				yield return "sendtochaterror Invalid duration.";
				yield break;
			} //If it's valid and in range, start holding
			Buttons[10].OnInteract();
			do { //Do-while used so stopwatch has time to reset if needed
				yield return new WaitForSeconds(0.1f);
				yield return "trycancel";
				if(locked) //Stop holding if inputs get locked
					break; //(May toggle mute, but whatever.)
			} while(tlHoldTime.ElapsedMilliseconds < 1000 * duration + 250); //200-300ms extra for safety
			//Release and check current locked status
			Buttons[10].OnInteractEnded();
			if(locked) {
				yield return "sendtochaterror The module locked inputs while holding TL.";
				yield break;
			} //Done holding, wait a bit before pressing BR
			yield return new WaitForSeconds(0.25f);
			yield return null;
			Buttons[13].OnInteract();
			yield break;
		}

		if(command.StartsWith("press ")) { //Trimming and/or waiting
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: 'press' removed in simplification", _moduleId);
			command = command.Replace("press ", "").Trim();
		} //Regex: a display, followed by whitespace, then "at", then more whitespace
		if(Regex.IsMatch(command, "^(tl|mute|tr|submit|destroy|destructinate|bl|split|br|clear)\\s+at\\s+")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: 'at <time>' processed and removed in simplification", _moduleId);
			int index = command.IndexOf("at");
			string end = command.Substring(index + 2).Trim();
			command = command.Substring(0, index).Trim();
			long press_time; //Attempt to parse the pressing time
			bool parsed = long.TryParse(end, out press_time);
			if(!parsed || press_time < INITIAL_TIME || press_time >= FULL_RESET_TIME) {
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: given time '{1}' bad (INVALID)", _moduleId, end);
				yield return "sendtochaterror Invalid pressing time.";
				yield break;
			} //Time must be reachable without resetting
			if(t > press_time) {
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: current time '{1}' later than given time '{2}' (INVALID)", _moduleId, t, press_time);
				yield return "sendtochaterror This time has already passed.";
				yield break;
			} //If it's valid, in range, and reachable, start waiting
			while(t != press_time && !locked) {
				yield return new WaitForSeconds(0.1f);
				yield return "trycancel";
			} //Check locked status
			if(locked) {
				yield return "sendtochaterror The module locked inputs while waiting.";
				yield break;
			} //Done waiting
			waited = true;
		}
		
		if(command.Equals("")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: empty command (INVALID)", _moduleId);
			yield return "sendtochaterror Invalid command.";
			yield break;
		} else if(Regex.IsMatch(command, "^(tl|mute)$")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: press TL", _moduleId);
			Buttons[10].OnInteract();
			yield return new WaitForSeconds(0.1f);
			yield return null;
			Buttons[10].OnInteractEnded();
			yield break;
		} else if(Regex.IsMatch(command, "^(tr|submit|destroy|destructinate)$")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: press TR", _moduleId);
			if(justStruck) { //Can't have just struck to submit
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: just struck (INVALID)", _moduleId);
				yield return "sendtochaterror You just submitted, you can't submit again yet.";
				yield break;
			} //Must also have specified a time to submit
			if(!waited) {
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: no time given (INVALID)", _moduleId);
				yield return "sendtochaterror Submitting without a time is not a good idea.";
				yield break;
			} //SUBMISSION HAPPENS HERE
			yield return null;
			long temp = k;
			if((t - (INITIAL_TIME % 210)) % 210 < 10 && grace_k != 0) {
				k = grace_k; //Grace period: submit with previous k
				Debug.LogFormat("[OmegaDestroyer #{0}]: Twitch Plays grace period activated!", _moduleId);
			} //(grace_k == 0 indicates there is no previous k)
			Buttons[11].OnInteract();
			k = temp; //Return to normal k when done
			yield break;
		} else if(Regex.IsMatch(command, "^(bl|split)$")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: press BL", _moduleId);
			yield return null;
			Buttons[12].OnInteract();
			yield break;
		} else if(Regex.IsMatch(command, "^(br|clear)$")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: press BR", _moduleId);
			yield return null;
			Buttons[13].OnInteract();
			yield break;
		} else if(Regex.IsMatch(command, "^\\d+$")) {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: press numbers", _moduleId);
			char[] presses = command.ToCharArray();
			if(input.Length + presses.Length > 8) {
				Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: input '{1}' plus presses '{2}' too long (INVALID)", _moduleId, input, command);
				yield return "sendtochaterror Sending this much input is not a good idea.";
				yield break;
			} //Press a button for each character
			for(int i = 0; i < presses.Length; i++) {
				yield return new WaitForSeconds(0.1f);
				Buttons[presses[i] - '0'].OnInteract();
			}
			yield return null;
			yield break;
		} else {
			Debug.LogFormat("[OmegaDestroyer #{0}]: Interpretation: unknown command (INVALID)", _moduleId);
			yield return "sendtochaterror Invalid command.";
			yield break;
		}
	}

	IEnumerator TwitchHandleForcedSolve() {
		muted = locked = true;
		Debug.LogFormat("[OmegaDestroyer #{0}]: ====TWITCH FORCE SOLVE====", _moduleId);
		Debug.LogFormat("[OmegaDestroyer #{0}]: A bypass has been detected. Disabling extra functionality...", _moduleId);
		mwyth = false; //mwythDeactivatable used to alter solve method
		deactivate = solved = true;
		if(!Application.isEditor)
			Audio.PlaySoundAtTransform("AlertTone", Numbers[0].transform);
		Debug.LogFormat("[OmegaDestroyer #{0}]: Extra functionality disabled. Module force-solved.", _moduleId);
		for(byte i = 0; i < Buttons.Length; i++) //Override colors
			BCChanger[i].material = BColors[0];
		for(byte i = NON_MAIN_NUMBERS - 1; i < Numbers.Length; i++)
			Numbers[i].color = Color.red;
		Numbers[0].text = "NO";
		Numbers[1].text = "TIME";
		Numbers[2].text = "BY";
		Numbers[3].text = "PA";
		Numbers[4].text = "SS";
		Numbers[5].text = "ED";
		Solve();
		yield break;
	}

#endregion

}