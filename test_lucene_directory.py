#!/usr/bin/env python3

import os
import sys

# Test the Lucene directory creation logic
def test_lucene_directory_creation():
    print("Testing Lucene directory creation logic...")
    
    # Simulate the Env.WorkDir path construction
    work_dir = os.path.join(os.path.expanduser("~"), ".local", "share", "TelegramSearchBot")
    print(f"Work directory: {work_dir}")
    
    # Check if work directory exists
    if os.path.exists(work_dir):
        print(f"✓ Work directory exists: {work_dir}")
    else:
        print(f"✗ Work directory does NOT exist: {work_dir}")
    
    # Check Index_Data directory
    index_data_dir = os.path.join(work_dir, "Index_Data")
    print(f"Index_Data directory: {index_data_dir}")
    
    if os.path.exists(index_data_dir):
        print(f"✓ Index_Data directory exists: {index_data_dir}")
        # List contents
        contents = os.listdir(index_data_dir)
        print(f"  Contents: {contents}")
    else:
        print(f"✗ Index_Data directory does NOT exist: {index_data_dir}")
    
    # Test directory creation
    test_group_id = 123456789
    group_index_dir = os.path.join(index_data_dir, str(test_group_id))
    print(f"Group index directory: {group_index_dir}")
    
    try:
        # Try to create the directory structure
        os.makedirs(group_index_dir, exist_ok=True)
        print(f"✓ Successfully created directory: {group_index_dir}")
        
        # Test permissions
        test_file = os.path.join(group_index_dir, "test_write.txt")
        with open(test_file, 'w') as f:
            f.write("test")
        print(f"✓ Successfully wrote test file: {test_file}")
        
        # Clean up
        os.remove(test_file)
        print(f"✓ Successfully cleaned up test file")
        
    except Exception as e:
        print(f"✗ Failed to create directory or write file: {e}")
    
    # Check current permissions
    try:
        stat_info = os.stat(work_dir)
        print(f"Work directory permissions: {oct(stat_info.st_mode)}")
    except Exception as e:
        print(f"Could not check permissions: {e}")

if __name__ == "__main__":
    test_lucene_directory_creation()