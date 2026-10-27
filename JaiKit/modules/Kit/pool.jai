// Pools of one item type with generational handles. Items need `alive: bool` and
// `generation: u32` (an embedded Actor has them). A removed item's slot is reused, and its
// generation bump makes old handles to it come back null.
//
// `*T` from a pool is good for the current frame; hold a Handle across frames.
// Adding to a pool while iterating that same pool may move its items.

Pool :: struct (T: Type) {
    items: [..] T;
    free: [..] s32;
}

Handle :: struct (T: Type) { index: s32 = -1; generation: u32; }

add :: (pool: *Pool($T)) -> *T, Handle(T) {
    index: s32;
    if pool.free.count index = pop(*pool.free);
    else { index = xx pool.items.count; array_add(*pool.items); }
    item := *pool.items[index];
    generation := item.generation;
    item.* = .{};
    item.generation = generation;
    item.alive = true;
    return item, .{index, generation};
}

free :: (pool: *Pool($T), item: *T) {
    if !item.alive return;
    item.alive = false;
    item.generation += 1;
    array_add(*pool.free, xx (item - pool.items.data));
}

get :: (pool: *Pool($T), handle: Handle(T)) -> *T {
    if handle.index < 0 || handle.index >= pool.items.count return null;
    item := *pool.items[handle.index];
    return ifx item.alive && item.generation == handle.generation then item else null;
}

handle_of :: (pool: *Pool($T), item: *T) -> Handle(T) {
    return .{xx (item - pool.items.data), item.generation};
}

clear :: (pool: *Pool) {
    for * pool.items if it.alive free(pool, it);
}

// for pool { ... } visits the live items as *T.
for_expansion :: (pool: *Pool, body: Code, flags: For_Flags) #expand {
    for * item, index: pool.items {
        if !item.alive continue;
        `it := item;
        `it_index := index;
        #insert body;
    }
}

#import "Basic";
