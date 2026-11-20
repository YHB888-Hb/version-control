# C# lock 

`lock` 是 C# 中用于线程互斥的关键字。

---

## 1. lock 解决什么问题

多个线程访问同一个共享资源时，如果这个操作不是一步完成的，就可能发生竞态。

例如：

```csharp
_count++;
```

这一行看起来简单，实际包含“读取、加一、写回”三个步骤。两个线程同时执行时，可能都读到旧值，最后只增加了一次。

`lock` 的作用，就是把一段代码标记为临界区：

```csharp
lock (_gate)
{
    _count++;
}
```

同一时刻，只有一个线程能进入这段代码。其他线程必须等待，直到锁被释放。

---

## 2. lock 的基本语法

### 2.1 声明锁对象

```csharp
private readonly object _sendLock = new object();
private readonly object _fileLock = new object();
```

锁对象通常满足三个条件：

1. 是引用类型。
2. 所有需要互斥的线程使用同一个对象。
3. 声明为 `private readonly`，不要中途替换。

值类型不能作为 `lock` 的目标。

### 2.2 使用 lock

```csharp
lock (_sendLock)
{
    // 临界区
}
```

进入 `lock` 后，当前线程持有锁；离开 `lock` 时，锁自动释放。

不要在每次调用时创建新的锁对象：

```csharp
lock (new object())   // 错误：每次都是新对象，等于没有锁
{
}
```

也不要锁 `this`、字符串字面量或 `typeof(...)`。这些对象可能被其他代码共用，容易产生意外的竞争。

### 2.3 编译器展开后的样子

对于普通的 `object` 锁对象，`lock` 是语法糖，展开后大致相当于：

```csharp
object obj = _sendLock;
bool lockTaken = false;

try`
{
    Monitor.Enter(obj, ref lockTaken);
    // 临界区
}
finally
{
    if (lockTaken)
    {
        Monitor.Exit(obj);
    }
}
```

`lock` 会自动生成 `try/finally`，所以临界区即使抛出异常，锁也能被释放。

---

## 3. lock 的原理和特点

### 3.1 基于 Monitor

`lock` 背后使用的是 `System.Threading.Monitor`：

- `Monitor.Enter` 尝试获得锁。
- `Monitor.Exit` 释放锁。
- 两者必须由同一个线程成对调用。

没有竞争时，获取锁的速度很快；发生竞争时，等待的线程会进入等待状态，直到锁被释放。

### 3.2 可重入

同一个线程可以多次进入同一把锁：

```csharp
lock (_sendLock)
{
    lock (_sendLock)
    {
        // 同一个线程，允许
    }
}
```

进入几次，就要退出几次，锁才会真正释放。

### 3.3 内存可见性

`lock` 除了互斥，还提供内存同步：

- 线程退出锁之前的写入，对下一个获得同一把锁的线程可见。
- 但只有使用同一把锁的代码才能获得这个保证。

如果另一个线程不经过锁直接读共享变量，仍然可能读到旧值。

### 3.4 不保证公平

`lock` 不保证先来先得。线程 A 释放锁后，等待中的 B、C 谁先获得锁没有严格保证。

因此，不能用 `lock` 来实现“消息按调用顺序发送”。需要严格顺序时，应使用队列或单写线程。

---

## 4. 关键：锁的范围要覆盖完整操作

这是使用 `lock` 最重要的地方。

发送消息时，如果一条消息不是一次写完，而是分三次写入：

```csharp
private void WriteParts(string sender, int index)
{
    Write(sender + index.ToString("00") + ":");
    Thread.Sleep(2);
    Write(new string(sender[0], 10));
    Thread.Sleep(2);
    Write(Environment.NewLine);
}
```

一条完整消息由“前缀 + 正文 + 换行”组成。

如果只锁住其中一次 `Write`：

```csharp
// 错误示例：只锁住了第一段
lock (_sendLock)
{
    Write(sender + index.ToString("00") + ":");
}

Write(new string(sender[0], 10));
Write(Environment.NewLine);
```

那么其他线程仍然可以插入到后半段，消息还是会被拆开。

正确的做法是把整个 `WriteParts` 调用放进锁里：

```csharp
if (useLock)
{
    lock (_sendLock)
    {
        WriteParts(sender, i);
    }
}
else
{
    WriteParts(sender, i);
}
```

这样，A 的三次写入是一个整体，B 不能插入到中间。

这个原则不仅适用于发送消息，也适用于写文件、更新多个字段、修改状态机等任何“必须一起完成”的操作。

---

## 5. 多个线程同时发送消息

A、B 两个并发任务各发送 20 条消息：

```csharp
Task.WaitAll(
    Task.Run(() => SendWorker("A", useLock)),
    Task.Run(() => SendWorker("B", useLock)));
```

`SendWorker` 根据 `useLock` 决定是否加锁：

```csharp
private void SendWorker(string sender, bool useLock)
{
    for (int i = 1; i <= MessageCount; i++)
    {
        if (useLock)
        {
            lock (_sendLock)
            {
                WriteParts(sender, i);
            }
        }
        else
        {
            WriteParts(sender, i);
        }

        Thread.Sleep(1);
    }
}
```

这里的 `_sendLock` 是 `Form1` 的字段，A、B 两个任务使用的是同一个锁对象，所以互斥成立。

如果每个任务各自创建一个锁对象，锁对象不同，线程之间不会互相等待，互斥就不会发生。

未加锁时，A、B 的三次写入可能交错，服务器可能收到类似下面的内容：

```text
A01:B01:AAAAAAAAAABBBBBBBBBB
```

加锁后，每条消息的三段内容会连续写入。

需要注意：`lock` 只保证“同一条消息的写入不会被另一个线程插入”，不保证消息到达服务器的顺序，也不保证消息一定送达。这些属于网络协议和业务层要处理的问题。

---

## 6. 示例二：多个线程同时写文件

写文件时，A、B 两个并发任务共用同一个 `StreamWriter`：

```csharp
using (var writer = new StreamWriter(_fileName, true, Encoding.UTF8) { AutoFlush = true })
{
    Task.WaitAll(
        Task.Run(() => WriteLines(writer, "A", useLock)),
        Task.Run(() => WriteLines(writer, "B", useLock)));
}
```

`StreamWriter` 不是线程安全的。多个线程同时调用它的 `Write` / `WriteLine`，可能造成内容交错、空行、内容缺失，甚至抛出异常。

`WriteLines` 中的加锁方式与发送消息相同：

```csharp
if (useLock)
{
    lock (_fileLock)
    {
        WriteLineParts(writer, sender, i);
    }
}
else
{
    WriteLineParts(writer, sender, i);
}
```

`WriteLineParts` 同样把一行拆成三段：

```csharp
writer.Write(sender + index.ToString("00") + ":");
Thread.Sleep(2);
writer.Write(new string(sender[0], 10));
Thread.Sleep(2);
writer.WriteLine();
```

未加锁时，文件中可能出现：

```text
A01:B01:AAAAAAAAAABBBBBBBBBB
```

加锁后，一整行文本的三段写入不会被另一个线程插入。

---

## 7. 两个示例的对比

| 场景 | 共享资源 | 锁对象 | 锁住的操作 | 不加锁时可能的结果 |
|---|---|---|---|---|
| 发送消息 | `NetworkStream _stream` | `_sendLock` | 一条消息的三段写入 | 消息内容交错 |
| 写文件 | `StreamWriter` | `_fileLock` | 一行文本的三段写入 | 行内容交错、空行、内容缺失 |

这两个例子的共同点不是“网络”或“文件”，而是：

- 有一个被多个线程共享的资源。
- 对资源的操作由多个步骤组成。
- 这些步骤必须作为一个整体完成。

满足这三点时，就需要考虑用 `lock` 保护整个操作。

---

## 8. 常见错误

### 8.1 每次创建新的锁对象

```csharp
lock (new object())
{
}
```

每个线程锁的都是不同对象，等于没有互斥。

### 8.2 每个线程使用不同的锁对象

例如任务 A 锁 `_lockA`，任务 B 锁 `_lockB`。锁对象不同，线程之间不会互相等待。

### 8.3 只锁住操作的一部分

只锁 `Write` 的一次调用，却把同一条消息的其余写入放在锁外，仍然会交错。

### 8.4 锁住不该锁的对象

```csharp
lock (this) { }
lock ("text") { }
lock (typeof(Form1)) { }
```

这些对象可能被外部代码共用，容易产生意外竞争或死锁。

### 8.5 在 lock 中 await

```csharp
lock (_gate)
{
    await Task.Delay(100); // 编译错误
}
```

`lock` 的所有权属于线程，`await` 之后可能切换到另一个线程。

### 8.6 认为 lock 能保证顺序

`lock` 只保证互斥，不保证线程按调用顺序获得锁。需要顺序时，使用队列或单写线程。

### 8.7 认为 lock 能解决所有并发问题

`lock` 不能保证：

- 消息按顺序到达
- TCP 的消息边界
- 消息一定送达
- 文件内容真正落盘
- 跨进程互斥

它只解决同一进程内、使用同一锁对象的线程互斥问题。

---

## 9. 总结

`lock` 的核心可以概括为三句话：

1. **找到共享资源**：哪些对象会被多个线程同时访问。
2. **确定完整操作**：哪一段代码必须作为一个整体执行。
3. **使用同一个锁对象包住它**：所有访问这个资源的代码都要遵守同一把锁。

在 `Lock应用` 中：

- `_sendLock` 保护一条消息的完整发送。
- `_fileLock` 保护一行文本的完整写入。

`lock` 不是“让代码变慢的开关”，也不是“自动解决并发”的魔法。它只是一个协作式互斥机制：只有所有相关代码都使用同一把锁，保护才真正成立。

本文示例源码：[Form1.cs](<C:\Users\Administrator\Desktop\Lock应用\Lock应用\Form1.cs>)
