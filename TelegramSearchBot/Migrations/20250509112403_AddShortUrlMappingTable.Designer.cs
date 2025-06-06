﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TelegramSearchBot.Model;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    [DbContext(typeof(DataDbContext))]
    [Migration("20250509112403_AddShortUrlMappingTable")]
    partial class AddShortUrlMappingTable
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.0");

            modelBuilder.Entity("TelegramSearchBot.Model.Data.AppConfigurationItem", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("TEXT");

                    b.Property<string>("Value")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("AppConfigurationItems");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.ChannelWithModel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("LLMChannelId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("ModelName")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("ChannelsWithModel");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.GroupData", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsBlacklist")
                        .HasColumnType("INTEGER");

                    b.Property<bool?>("IsForum")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Title")
                        .HasColumnType("TEXT");

                    b.Property<string>("Type")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("GroupData");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.GroupSettings", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsManagerGroup")
                        .HasColumnType("INTEGER");

                    b.Property<string>("LLMModelName")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("GroupId")
                        .IsUnique();

                    b.ToTable("GroupSettings");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.LLMChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ApiKey")
                        .HasColumnType("TEXT");

                    b.Property<string>("Gateway")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<int>("Parallel")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Priority")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Provider")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("LLMChannels");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.Message", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Content")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DateTime")
                        .HasColumnType("TEXT");

                    b.Property<long>("FromUserId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("MessageId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("ReplyToMessageId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("ReplyToUserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("Messages");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.ShortUrlMapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("CreationDate")
                        .HasColumnType("TEXT");

                    b.Property<string>("LongUrl")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("ShortCode")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("ShortCode")
                        .IsUnique();

                    b.ToTable("ShortUrlMappings");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.UserData", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("FirstName")
                        .HasColumnType("TEXT");

                    b.Property<bool?>("IsBot")
                        .HasColumnType("INTEGER");

                    b.Property<bool?>("IsPremium")
                        .HasColumnType("INTEGER");

                    b.Property<string>("LastName")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserName")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("UserData");
                });

            modelBuilder.Entity("TelegramSearchBot.Model.Data.UserWithGroup", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("UserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("UsersWithGroup");
                });
#pragma warning restore 612, 618
        }
    }
}
