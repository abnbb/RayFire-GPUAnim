using UnityEngine;

public class BreakAnimPlayer : MonoBehaviour
{
    public Animator anim;
    public string breakAnimName = "Take 001";

    void Start()
    {
        if (anim == null)
        {
            anim = GetComponent<Animator>();
        }
    }

    void Update()
    {
        // 按下空格键触发破碎
        if (Input.GetKeyDown(KeyCode.Space))
        {
            PlayBreak();
        }
    }

    // 触发破碎
    public void PlayBreak()
    {
        anim.Play(breakAnimName, 0, 0f); // 0是图层，0从头开始
        anim.speed = 1;
    }

    // 暂停
    public void PauseBreak()
    {
        anim.speed = 0;
    }

    // 倒放还原完整模型
    public void ReverseBreak()
    {
        anim.Play(breakAnimName, 0, 1f);
        anim.speed = -1;
    }

    // 跳到动画中途（例如破碎50%）
    public void JumpHalfBreak()
    {
        anim.Play(breakAnimName, 0, 0.5f);
        anim.speed = 0;
    }
}