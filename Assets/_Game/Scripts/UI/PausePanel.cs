using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SuperSmashLike.Core;

namespace SuperSmashLike.UI
{
    public class PausePanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button moveListButton;

        [Header("Settings")]
        [Tooltip("「设置」按钮：打开全屏设置面板（M8-D3/D4）")]
        [SerializeField] private Button settingsButton;
        [Tooltip("设置面板：打开时本脚本的方向键导航停摆，输入全部交给面板")]
        [SerializeField] private SettingsPanel settingsPanel;

        [Header("Move List")]
        [SerializeField] private MoveListUI moveListUI;

        [Header("Navigation")]
        [Tooltip("方向键在按钮间切换的手柄重复延迟（秒）")]
        [SerializeField] private float navRepeatDelay = 0.18f;
        [Tooltip("方向键选中的按钮高亮色（明显区别于未选中态）")]
        [SerializeField] private Color selectionColor = new Color(1f, 0.85f, 0.2f, 1f);

        #endregion

        #region 2. 内部状态

        private Button[] _navButtons;
        private int _navIndex;
        private float _nextNavTime;
        private bool _navSelectionActive = true;

        #endregion

        #region 3. Unity 生命周期

        private void Start()
        {
            // 【注意】一律空引用守护 + 提示日志，对齐 TitlePanel 的写法
            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResume);
            if (restartButton != null)
                restartButton.onClick.AddListener(OnRestart);
            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuit);
            if (moveListButton != null)
                moveListButton.onClick.AddListener(OnMoveList);
            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnSettings);

            BuildNavButtons();
        }

        // 每次暂停面板弹出 → 出招表复位为关闭态 + 默认选中第一个按钮
        private void OnEnable()
        {
            if (moveListUI != null) moveListUI.Close();
            // 设置面板同理：重新弹出暂停菜单时回到"设置已关闭"状态（M8-D3/D4）
            if (settingsPanel != null) settingsPanel.Close();

            if (_navButtons == null) BuildNavButtons();
            _navSelectionActive = true;
            SelectButton(0);
        }

        // 离开暂停面板（继续/重开/退出）→ 关闭出招表，避免残留在战斗画面上
        private void OnDisable()
        {
            if (moveListUI != null) moveListUI.Close();
        }

        private void Update()
        {
            if (GameManager.Instance == null ||
                GameManager.Instance.CurrentGameState != GameState.Paused) return;
            if (_navButtons == null || _navButtons.Length == 0) return;

            var gp = Gamepad.current;

            // 出招表打开时：方向键归它切换 Tab；K/小键盘2/手柄B=返回，关闭出招表；
            // 同时清空事件系统选中态，避免按确认键误触发暂停菜单按钮（如误触"继续"）
            if (moveListUI != null && moveListUI.gameObject.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Keypad2) ||
                    (gp != null && gp.buttonEast.wasPressedThisFrame))
                    moveListUI.Close();

                if (_navSelectionActive)
                {
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    _navSelectionActive = false;
                }
                return;
            }

            // 设置面板打开时（M8-D3/D4）：输入全部交给面板（关闭键/重绑定），
            // 本脚本导航停摆并清掉选中高亮，避免按确认键误触"继续"
            if (settingsPanel != null && settingsPanel.IsOpen)
            {
                if (_navSelectionActive)
                {
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    _navSelectionActive = false;
                }
                return;
            }
            if (!_navSelectionActive)
            {
                SelectButton(_navIndex);
                _navSelectionActive = true;
            }

            // 确认 / 返回：键盘 J/小键盘1=确认、K/小键盘2=返回；手柄 A=确认、B=返回。
            // 这样暂停菜单可以纯键盘/手柄操作，不必依赖鼠标点击。
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Keypad1) ||
                (gp != null && gp.buttonSouth.wasPressedThisFrame))
            {
                ConfirmNav();
                return;
            }
            if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Keypad2) ||
                (gp != null && gp.buttonEast.wasPressedThisFrame))
            {
                BackNav();
                return;
            }

            int dir = 0;

            // 键盘：上下左右（含 WASD）任一键都可切换；左/上=上一个，右/下=下一个
            if (Input.GetKeyDown(KeyCode.UpArrow)    || Input.GetKeyDown(KeyCode.W) ||
                Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A))
                dir = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow)  || Input.GetKeyDown(KeyCode.S) ||
                     Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                dir = 1;

            // 手柄：摇杆优先，其次十字键；边沿 + 重复延迟（防一帧跳多个）
            if (dir == 0 && gp != null && Time.unscaledTime >= _nextNavTime)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;

                if      (nav.y >  0.5f || nav.x < -0.5f) dir = -1;
                else if (nav.y < -0.5f || nav.x >  0.5f) dir = 1;
            }

            if (dir != 0)
            {
                _navIndex = (_navIndex + dir + _navButtons.Length) % _navButtons.Length;
                SelectButton(_navIndex);
                _nextNavTime = Time.unscaledTime + navRepeatDelay;
            }
        }

        #endregion

        #region 4. 方向键导航

        private void BuildNavButtons()
        {
            // 【顺序】与菜单里的视觉顺序（自上而下）严格一致：
            //   设置(最上) → 出招表 → 继续 → 重新开始 → 返回标题(下)
            //   这样按"上/下"高亮才会跟着按钮的实际位置走。（M8-D3/D4：设置置顶）
            var list = new System.Collections.Generic.List<Button>(5);
            if (settingsButton != null) list.Add(settingsButton);
            if (moveListButton != null) list.Add(moveListButton);
            if (resumeButton != null)   list.Add(resumeButton);
            if (restartButton != null)  list.Add(restartButton);
            if (quitButton != null)     list.Add(quitButton);
            _navButtons = list.ToArray();
            _navIndex = 0;

            // 关闭 UGUI 自动导航：方向切换完全由本脚本接管，
            // 否则 StandaloneInputModule 会自动导航 → 与这里双重移动。
            for (int i = 0; i < _navButtons.Length; i++)
            {
                var b = _navButtons[i];

                // 关闭 UGUI 自动导航：方向切换完全由本脚本接管，
                // 否则 StandaloneInputModule 会自动导航 → 与这里双重移动。
                var nav = b.navigation;
                nav.mode = Navigation.Mode.None;
                b.navigation = nav;

                // 选中态用明显高亮色，方便方向键操作时一眼看出当前按钮
                var cb = b.colors;
                cb.normalColor      = Color.white;
                cb.selectedColor    = selectionColor;
                cb.highlightedColor = selectionColor;
                cb.pressedColor     = selectionColor * 0.8f;
                cb.colorMultiplier  = 1f;
                cb.fadeDuration     = 0.05f;
                b.colors = cb;

                // 鼠标悬停 → 把"键盘选中"移到该按钮：与方向键共用同一个高亮，
                // 避免出现"鼠标一个高亮 + 方向键一个高亮"两个并存的情况。
                int captured = i;
                var trig = b.gameObject.GetComponent<EventTrigger>();
                if (trig == null) trig = b.gameObject.AddComponent<EventTrigger>();
                trig.triggers.Clear();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ => OnNavHover(captured));
                trig.triggers.Add(entry);
            }
        }

        // 鼠标悬停到某个按钮 → 同步键盘选中态（两者共用同一个高亮）
        private void OnNavHover(int index)
        {
            if (_navButtons == null || index < 0 || index >= _navButtons.Length) return;
            if (moveListUI != null && moveListUI.gameObject.activeSelf) return;  // 出招表打开时不管
            if (_navButtons[index] == null) return;

            _navIndex = index;
            _navSelectionActive = true;
            SelectButton(index);
        }

        private void SelectButton(int i)
        {
            if (_navButtons == null || _navButtons.Length == 0) return;
            if (i < 0 || i >= _navButtons.Length) return;
            if (EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(_navButtons[i].gameObject);
        }

        // 确认：触发当前选中按钮（键盘 J / 小键盘1、手柄 A）
        private void ConfirmNav()
        {
            if (_navButtons == null || _navIndex < 0 || _navIndex >= _navButtons.Length) return;
            var b = _navButtons[_navIndex];
            if (b != null) b.onClick.Invoke();
        }

        // 返回：关闭暂停菜单 = 恢复比赛（键盘 K / 小键盘2、手柄 B）
        private void BackNav()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.ResumeGame();
        }

        #endregion

        #region 5. 按钮动作

        // 【做什么】继续比赛 —— 直接调已有的 ResumeGame（同暂停键逻辑）
        private void OnResume()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.ResumeGame();
        }

        // 【做什么】重新开始 —— 参考 ResultsPanel.OnRematch 的写法，
        //   场景名是否有效需按你项目实际场景确认（项目当前只有一个 BattleTest 场景）
        private void OnRestart()
        {
            GameManager.Instance.ResumeGame();   // 先恢复 timeScale，避免新场景冻结
            GameManager.Instance.SwitchState(GameState.CharacterSelect);
        }

        // 【做什么】打开设置面板 —— 只开面板，绝不 ResumeGame，游戏保持暂停（M8-D3/D4）
        private void OnSettings()
        {
            if (settingsPanel != null) settingsPanel.Open();
        }

        // 【做什么】打开出招表 —— 注意：绝不调 ResumeGame，游戏要保持暂停
        private void OnMoveList()
        {
            GameSettings gs = GameManager.Instance.gameSettings;

            // 优先取选人缓存；没走过选人流程时回退到场上第一个角色
            var fd = gs.GetSelection(0);
            if (fd == null && GameManager.Instance.ActivePlayers.Count > 0)
                fd = GameManager.Instance.ActivePlayers[0].fighterData;

            // 战斗里的出招表默认展示 P1（暂停是全局的，没有"谁按的暂停"）；
            // 传 playerId=0 才会显示"P1"标签，否则标签为空。
            // allowPlayerSwitch=true → 打开后可按 Q/E（手柄 LB/RB）切换到 P2 的出招表。
            if (moveListUI != null) moveListUI.ShowFor(fd, 0, true);
        }


        // 【做什么】返回标题 —— 同上思路
        private void OnQuit()
        {
            GameManager.Instance.ResumeGame();   // 先恢复 timeScale，避免标题界面冻结
            GameManager.Instance.SwitchState(GameState.Title);
        }

        #endregion
    }
}
