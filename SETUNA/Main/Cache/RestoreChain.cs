using System;

namespace SETUNA.Main.Cache
{
    /// <summary>
    /// 逐项驱动的恢复链。每一项处理完后调用 <c>advance</c> 推进到下一项，
    /// 全部处理完调用 <c>completed</c>。
    /// <para>
    /// 保证三件事：每一项最多推进一次（重复回调被吸收，不会跳项）；
    /// 处理某项时抛异常会跳过该项继续；无论中间失败多少项，
    /// <c>completed</c> 最终恰好调用一次。缺了任何一条，
    /// <see cref="CacheManager.IsInit"/> 就可能永不置位，
    /// 而等待它的延迟初始化定时器会无限轮询。
    /// </para>
    /// </summary>
    public static class RestoreChain
    {
        /// <param name="count">待处理的项数。</param>
        /// <param name="step">
        /// 处理第 index 项；完成时调用第二个参数推进。可以同步或异步调用。
        /// </param>
        /// <param name="completed">全部处理完后调用，恰好一次。</param>
        public static void Run(int count, Action<int, Action> step, Action completed)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            var index = 0;
            Pump();

            void Pump()
            {
                while (index < count)
                {
                    var current = index;
                    var advanced = false;
                    var advancedSynchronously = false;
                    var insideStep = true;

                    void Advance()
                    {
                        // 每项只推进一次：重复回调会跳过一项缓存。
                        if (advanced)
                        {
                            return;
                        }

                        advanced = true;
                        index = current + 1;

                        if (insideStep)
                        {
                            // 同步回调：交给外层循环继续，避免链长时递归过深。
                            advancedSynchronously = true;
                        }
                        else
                        {
                            // 异步回调（样式应用由定时器驱动）：重新启动泵。
                            Pump();
                        }
                    }

                    try
                    {
                        step(current, Advance);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"RestoreChain: item {current} failed: {ex}");
                        Advance();
                    }
                    finally
                    {
                        insideStep = false;
                    }

                    if (!advancedSynchronously)
                    {
                        // 等异步回调把泵重新启动。
                        return;
                    }
                }

                completed?.Invoke();
            }
        }
    }
}
