using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SuperSmashLike.Core;   // AudioManager / SfxId

// ============================================================
// UiAudioBinder — UI 音效绑定器（M8-D2）
// 职责：On Start 扫描自身 Canvas 祖先下的全部 Button / Slider，
//   一次性挂上音效监听（不逐个改现有 UI 面板代码）：
//   - Button.onClick      → UiConfirm（音量 0.6）
//   - Slider.onValueChanged → UiMove（音量 0.25，0.08s 节流，防拖动每帧连响）
// 使用方式：把本组件挂在战斗/菜单 Canvas 上（由 SetupWizard 自动挂载）
// 【重点】幂等：_bound 防整体重入；_boundIds 防单控件重复 AddListener ——
//   重复 Bind 不会重复挂监听，不会出现一次点击播两声
// 【重点】判空：AudioManager 缺失时 AudioManager.Ui 静默 no-op，调用点无需额外告警
// ============================================================
namespace SuperSmashLike.UI
{
    public class UiAudioBinder : MonoBehaviour
    {
        #region 1. 配置常量

        private const float ButtonVolume = 0.6f;      // 按钮确认音量
        private const float SliderVolume = 0.25f;     // 滑条音量
        private const float SliderThrottle = 0.08f;   // 滑条音效最小间隔（秒，unscaled）

        #endregion

        #region 2. 运行时状态

        private bool _bound;                                // 整体绑定幂等标记
        private readonly HashSet<int> _boundIds = new HashSet<int>();   // 已绑定控件的 instanceID
        private float _lastSliderSfxTime = -999f;           // 上次滑条音效时刻（初值远早于开局，保证首动必响）

        #endregion

        #region 3. 生命周期

        private void Start()
        {
            Bind();
        }

        #endregion

        #region 4. 绑定逻辑

        // 【做什么】扫描自身 Canvas 祖先下的全部 Button/Slider 并挂音效监听
        // 【幂等】_bound 防整体重入；_boundIds 防同一控件重复 AddListener
        // 【注意】GetComponentsInChildren(true) 含未激活面板（暂停/结算等），一次挂全
        private void Bind()
        {
            if (_bound) return;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;   // 不在 Canvas 下：不绑定（静默，不报错刷屏）

            _bound = true;

            foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
            {
                if (button == null) continue;
                if (!_boundIds.Add(button.GetInstanceID())) continue;   // 已绑定 → 跳过

                button.onClick.AddListener(() => AudioManager.Ui(SfxId.UiConfirm, ButtonVolume));
            }

            foreach (Slider slider in canvas.GetComponentsInChildren<Slider>(true))
            {
                if (slider == null) continue;
                if (!_boundIds.Add(slider.GetInstanceID())) continue;   // 已绑定 → 跳过

                slider.onValueChanged.AddListener(_ =>
                {
                    // 【重点】节流：拖动滑条几乎每帧都触发，0.08s 内只放一次音；
                    //   用 unscaledTime —— 暂停菜单 timeScale=0 时普通 Time.time 不走
                    if (Time.unscaledTime - _lastSliderSfxTime < SliderThrottle) return;
                    _lastSliderSfxTime = Time.unscaledTime;
                    AudioManager.Ui(SfxId.UiMove, SliderVolume);
                });
            }
        }

        #endregion
    }
}
