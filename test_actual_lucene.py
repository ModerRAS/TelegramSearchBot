#!/usr/bin/env python3

import os
import sys

# Test the actual Lucene directory creation logic as used in the application
def test_actual_lucene_logic():
    print("Testing actual LuceneManager directory creation logic...")
    
    # Simulate the exact path construction from LuceneManager.GetFSDirectory
    work_dir = os.path.join(os.path.expanduser("~"), ".local", "share", "TelegramSearchBot")
    index_data_dir = os.path.join(work_dir, "Index_Data")
    
    print(f"Work directory: {work_dir}")
    print(f"Index_Data directory: {index_data_dir}")
    
    # Test various group IDs
    test_groups = [123456789, -1001234567890, 0, 1]
    
    for group_id in test_groups:
        group_dir = os.path.join(index_data_dir, str(group_id))
        print(f"\nTesting group {group_id}:")
        print(f"  Group index directory: {group_dir}")
        
        # Check if directory exists
        if os.path.exists(group_dir):
            print(f"  ✓ Directory exists")
            contents = os.listdir(group_dir)
            if contents:
                print(f"  ✓ Directory contains: {contents}")
            else:
                print(f"  ⚠ Directory exists but is empty")
        else:
            print(f"  ✗ Directory does not exist")
            
            # Test if we can create it
            try:
                os.makedirs(group_dir, exist_ok=True)
                print(f"  ✓ Successfully created directory")
                
                # Test write permissions
                test_file = os.path.join(group_dir, "segments.gen")
                with open(test_file, 'w') as f:
                    f.write("test")
                print(f"  ✓ Successfully wrote test file")
                
                # Clean up
                os.remove(test_file)
                
            except Exception as e:
                print(f"  ✗ Failed: {e}")
    
    # Check overall permissions
    try:
        stat_info = os.stat(work_dir)
        print(f"\nWork directory permissions: {oct(stat_info.st_mode)}")
        print(f"Work directory owner: {stat_info.st_uid}:{stat_info.st_gid}")
    except Exception as e:
        print(f"Could not check permissions: {e}")

if __name__ == "__main__":
    test_actual_lucene_logic()