
# 🕹️ AutoSol — Unity C# ➝ Solidity Autopilot

AutoSol is a Unity plugin that converts **C# contracts** into **Solidity smart contracts**, deploys them to **Somnia blockchain**, and auto-replaces your C# calls with contract calls.  
No Solidity needed 🚀

---

## ⚙️ How to Install

1. **Build DLL**
   - Open solution in Visual Studio (set to `.NET Framework 4.x`).
   - Build in **Release** mode.
   - Get `AutoSol.dll` from `bin/Release`.

2. **Add to Unity**
   - Create folder: `Assets/Plugins`
   - Copy `AutoSol.dll` (+ `Newtonsoft.Json.dll`) into it.
   - Restart Unity.

---

## 🎮 Usage

1. Create folder: `web3`
2. Add C# contracts (interface + class):
   ```csharp
   public interface IHighScore {
       void SetHighScore(int score);
       int GetHighScore();
   }

   public class HighScore : IHighScore {
       private int highScore;
       public void SetHighScore(int score) => highScore = score;
       public int GetHighScore() => highScore;
   }
3. Create a new .config file and add address to it if you want to interact with an already deployed contract, if file not present, plugin will deploy a new contract instance, create a new .config file and add address to it.
4. Run/Build Unity Project